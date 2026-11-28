using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace MangaOrganizer.Server;

internal sealed partial class LibraryStore
{
    public ScanSnapshot? ReadPausedScan(Guid rootId)
    {
        using var database = OpenDatabase();
        using var command = database.CreateCommand();
        command.CommandText = """
            SELECT checkpoint.snapshot FROM library_scan_checkpoint checkpoint
            JOIN library_root root ON root.id = checkpoint.root_id
            WHERE root.id = $id AND root.state = 'paused';
            """;
        command.Parameters.AddWithValue("$id", rootId);
        return command.ExecuteScalar() is string json ? JsonSerializer.Deserialize<ScanSnapshot>(json) : null;
    }

    public bool StopPausedScan(Guid rootId)
    {
        using var database = OpenDatabase();
        using var transaction = database.BeginTransaction();
        using var command = database.CreateCommand();
        command.Transaction = transaction;
        command.Parameters.AddWithValue("$id", rootId);
        command.CommandText = "UPDATE library_root SET state = 'stopped' WHERE id = $id AND state = 'paused';";
        if (command.ExecuteNonQuery() != 1) return false;
        command.CommandText = "UPDATE library_scan_checkpoint SET file_plan = NULL, skipped_links = NULL WHERE root_id = $id;";
        command.ExecuteNonQuery();
        transaction.Commit();
        return true;
    }

    private static void SaveScanCheckpoint(SqliteConnection database, Guid rootId, ScanSnapshot progress, string state, long? skippedLinks)
    {
        using var transaction = database.BeginTransaction();
        using var command = database.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT OR REPLACE INTO library_scan_checkpoint(root_id, snapshot, skipped_links, file_plan, digests)
            SELECT $id, $snapshot,
                CASE WHEN $state = 'paused' THEN COALESCE($skipped, (
                    SELECT saved.skipped_links FROM library_scan_checkpoint saved WHERE saved.root_id = $id)) END,
                CASE WHEN $state <> 'paused' THEN NULL WHEN $skipped IS NOT NULL THEN (
                    SELECT json_group_array(json_array(relative_path, extension, size, modified_at) ORDER BY relative_path)
                    FROM scan_file) ELSE (SELECT saved.file_plan FROM library_scan_checkpoint saved WHERE saved.root_id = $id) END,
                (SELECT json_group_array(json(value)) FROM (
                    SELECT json_array(h.relative_path, f.size, f.modified_at, f.volume_hint, f.chapter_hint,
                        hex(h.sha256), h.detected_chapters, h.chapter_source, h.stamp) AS value
                    FROM scan_hash h JOIN scan_file f USING (relative_path) WHERE h.stamp IS NOT NULL
                    UNION ALL
                    SELECT previous.value FROM library_scan_checkpoint saved, json_each(saved.digests) previous
                    WHERE saved.root_id = $id AND NOT EXISTS (
                        SELECT 1 FROM scan_hash h WHERE h.relative_path = previous.value ->> 0)
                ));
            UPDATE library_root SET state = $state WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", rootId);
        command.Parameters.AddWithValue("$snapshot", JsonSerializer.Serialize(progress));
        command.Parameters.AddWithValue("$skipped", (object?)(state == "paused" ? skippedLinks : null) ?? DBNull.Value);
        command.Parameters.AddWithValue("$state", state);
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    private static (long SkippedLinks, LibraryFileFact[] Files)? ReadScanPlan(SqliteConnection database, Guid rootId)
    {
        using var command = database.CreateCommand();
        command.CommandText = """
            SELECT skipped_links, file_plan FROM library_scan_checkpoint WHERE root_id = $id AND file_plan IS NOT NULL;
            """;
        command.Parameters.AddWithValue("$id", rootId);
        using var rows = command.ExecuteReader();
        if (!rows.Read()) return null;
        using var plan = JsonDocument.Parse(rows.GetString(1));
        return (rows.GetInt64(0), plan.RootElement.EnumerateArray().Select(file => new LibraryFileFact(
            file[0].GetString()!, file[1].GetString()!, file[2].GetInt64(),
            DateTimeOffset.Parse(file[3].GetString()!, System.Globalization.CultureInfo.InvariantCulture))).ToArray());
    }
}
