using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace MangaOrganizer.Server;

internal sealed record ResetScope(string Level, Guid[] RootIds);
internal sealed record ResetRows(string Label, long Count);
internal sealed record ResetPlan(ResetScope Scope, IReadOnlyList<ResetRows> Affected, string Fingerprint);
internal sealed record CompletedReset(Guid Id, string Level, IReadOnlyList<ResetRows> Affected, string BackupDirectory, DateTimeOffset CompletedAt);
internal sealed class MaintenanceResetException(string message) : Exception(message);

internal sealed partial class LibraryStore
{
    private sealed record ResetStep(string Table, string Label, string Condition, string Order, string? Assignment = null)
    {
        public string ReadSql => $"SELECT * FROM {Table} WHERE {Condition} ORDER BY {Order};";
        public string ApplySql => Assignment is null ? $"DELETE FROM {Table} WHERE {Condition};"
            : $"UPDATE {Table} SET {Assignment} WHERE {Condition};";
    }

    private const string SelectedRoot = "lower(root_id) IN (SELECT value FROM json_each($roots))";
    private const string SelectedSource = "lower(source_root) IN (SELECT value FROM json_each($roots))";
    private const string SelectedState = """
        lower(id) IN (SELECT value FROM json_each($roots))
        AND (state <> 'idle' OR file_count <> 0 OR last_attempt_at IS NOT NULL OR last_scanned_at IS NOT NULL OR skipped_links <> 0)
        """;
    private const string ExclusiveContent = """
        content_id IN (SELECT f.content_id FROM library_file f WHERE lower(f.root_id) IN (SELECT value FROM json_each($roots)))
        AND NOT EXISTS (SELECT 1 FROM library_file f WHERE f.content_id = content_metadata.content_id
            AND lower(f.root_id) NOT IN (SELECT value FROM json_each($roots)))
        """;

    private static readonly ResetStep[] CatalogReset =
    [
        new("content_metadata", "Series associations for files exclusive to the selected folders", ExclusiveContent, "content_id"),
        new("series_metadata_binding", "Series bindings for selected folders", SelectedRoot, "root_id, series_key"),
        new("library_series_cover", "Indexed series covers", SelectedRoot, "root_id, series_key"),
        new("import_file_split", "Import grouping decisions", "file_id IN (SELECT id FROM library_file WHERE " + SelectedRoot + ")", "file_id"),
        new("library_file", "Indexed files", SelectedRoot, "root_id, relative_path"),
        new("library_root", "Folder scan records", SelectedState, "id",
            "state = 'idle', file_count = 0, last_attempt_at = NULL, last_scanned_at = NULL, skipped_links = 0"),
    ];

    private static readonly ResetStep[] ProgressReset =
    [
        new("reading_progress_event", "Reading progress events", "owner_id = $owner", "content_id, update_id"),
        new("reading_progress", "Saved reading positions and file completion", "owner_id = $owner", "content_id"),
        new("chapter_completion", "Chapter reading status", "owner_id = $owner", "work_key, volume, chapter"),
    ];

    private static readonly ResetStep[] ConfigurationReset =
    [
        new("library_intake_target", "Saved intake targets for selected source folders", SelectedSource, "source_root"),
        new("library_folder", "Folder names, roles and managed access",
            SelectedRoot + " AND (name IS NOT NULL OR kind <> 'library' OR managed <> 0)", "root_id",
            "name = NULL, kind = 'library', managed = 0"),
    ];

    public ResetPlan PreviewReset(Guid owner, ResetScope scope)
    {
        using var database = OpenDatabase();
        using var transaction = database.BeginTransaction(deferred: true);
        using var command = ResetCommand(database, transaction, owner, scope);
        return InspectReset(command, scope);
    }

    public CompletedReset ApplyReset(Guid owner, ResetScope scope, string fingerprint)
    {
        using var database = OpenDatabase();
        using var transaction = database.BeginTransaction(deferred: false);
        using var command = ResetCommand(database, transaction, owner, scope);
        var plan = InspectReset(command, scope);
        if (plan.Fingerprint != fingerprint)
            throw new MaintenanceResetException("The selected data changed. Preview the reset again.");
        if (plan.Affected.All(item => item.Count == 0))
            throw new MaintenanceResetException("There are no matching changes to apply.");

        command.CommandText = "SELECT MAX(version) FROM schema_migration;";
        var schema = Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
        command.CommandText = "SELECT name FROM sqlite_schema WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";
        var tables = new List<string>();
        using (var rows = command.ExecuteReader())
            while (rows.Read()) tables.Add(rows.GetString(0));
        var backup = UpgradeBackup.Create(dataDirectory, command, tables, schema, schema, maintenance: true);

        foreach (var step in ResetSteps(scope.Level))
        {
            command.CommandText = step.ApplySql;
            command.ExecuteNonQuery();
        }

        var completed = new CompletedReset(Guid.NewGuid(), scope.Level, plan.Affected, Path.GetFileName(backup), DateTimeOffset.UtcNow);
        command.CommandText = """
            INSERT INTO maintenance_reset(id, owner_id, level, affected, backup_directory, completed_at)
            VALUES ($id, $owner, $level, $affected, $backup, $at);
            """;
        command.Parameters.AddWithValue("$id", completed.Id);
        command.Parameters.AddWithValue("$level", completed.Level);
        command.Parameters.AddWithValue("$affected", JsonSerializer.Serialize(completed.Affected));
        command.Parameters.AddWithValue("$backup", completed.BackupDirectory);
        command.Parameters.AddWithValue("$at", completed.CompletedAt);
        command.ExecuteNonQuery();
        transaction.Commit();
        return completed;
    }

    public IReadOnlyList<CompletedReset> ReadResets(Guid owner)
    {
        using var database = OpenDatabase();
        using var command = database.CreateCommand();
        command.CommandText = """
            SELECT id, level, affected, backup_directory, completed_at FROM maintenance_reset
            WHERE owner_id = $owner ORDER BY completed_at DESC LIMIT 20;
            """;
        command.Parameters.AddWithValue("$owner", owner);
        var result = new List<CompletedReset>();
        using var rows = command.ExecuteReader();
        while (rows.Read())
            result.Add(new(rows.GetGuid(0), rows.GetString(1), JsonSerializer.Deserialize<ResetRows[]>(rows.GetString(2)) ?? [],
                rows.GetString(3), rows.GetDateTimeOffset(4)));
        return result;
    }

    private static ResetPlan InspectReset(SqliteCommand command, ResetScope scope)
    {
        command.CommandText = """
            SELECT EXISTS(SELECT 1 FROM library_root WHERE state IN ('queued', 'scanning'))
                OR EXISTS(SELECT 1 FROM file_operation WHERE state IN ('applying', 'restoring'));
            """;
        if ((long)command.ExecuteScalar()! != 0)
            throw new MaintenanceResetException("Wait for library scans and file operations to finish.");
        command.CommandText = "SELECT COUNT(*) FROM library_folder WHERE " + SelectedRoot;
        if ((long)command.ExecuteScalar()! != scope.RootIds.Length)
            throw new MaintenanceResetException("A selected folder changed. Select the current folders and preview again.");

        using var fingerprint = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        fingerprint.AppendData(JsonSerializer.SerializeToUtf8Bytes(scope));
        var affected = new List<ResetRows>();
        foreach (var step in ResetSteps(scope.Level))
        {
            fingerprint.AppendData(JsonSerializer.SerializeToUtf8Bytes(step.Table));
            command.CommandText = step.ReadSql;
            using var rows = command.ExecuteReader();
            var values = new object[rows.FieldCount];
            long count = 0;
            while (rows.Read())
            {
                rows.GetValues(values);
                fingerprint.AppendData(JsonSerializer.SerializeToUtf8Bytes(values));
                count++;
            }
            affected.Add(new(step.Label, count));
        }
        return new(scope, affected, Convert.ToHexString(fingerprint.GetHashAndReset()));
    }

    private static ResetStep[] ResetSteps(string level) => level switch
    {
        "catalog" => CatalogReset,
        "progress" => ProgressReset,
        "configuration" => ConfigurationReset,
        _ => throw new ArgumentOutOfRangeException(nameof(level)),
    };

    private static SqliteCommand ResetCommand(SqliteConnection database, SqliteTransaction transaction, Guid owner, ResetScope scope)
    {
        var command = database.CreateCommand();
        command.Transaction = transaction;
        command.Parameters.AddWithValue("$owner", owner);
        command.Parameters.AddWithValue("$roots", JsonSerializer.Serialize(scope.RootIds));
        return command;
    }
}
