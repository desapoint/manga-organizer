using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace MangaOrganizer.Server;

internal sealed record AssociationSeries(string Id, string Title, string? AlternateTitle, string Kind, int? ProviderId);
internal sealed record AssociationLocation(Guid RootId, string Folder, string RelativePath);
internal sealed record AssociationPreview(string? CurrentTitle, AssociationSeries Target, int Count,
    IReadOnlyList<AssociationLocation> Locations, string Revision);
internal sealed record AssociationScope(Guid Root, string FileId, string ContentId, string SeriesKey, string MetadataId);

internal sealed partial class LibraryStore
{
    private const string AvailableAssociationSeries = """
         FROM series_metadata m WHERE (
            EXISTS(SELECT 1 FROM series_metadata_binding b JOIN library_folder folder ON folder.root_id = b.root_id WHERE b.metadata_id = m.id)
            OR EXISTS(SELECT 1 FROM content_metadata cm JOIN library_file f ON f.content_id = cm.content_id
                JOIN library_folder folder ON folder.root_id = f.root_id WHERE cm.metadata_id = m.id)
        )
        """;

    public CatalogPage<AssociationSeries> ReadAssociationSeries(string search, int offset, int limit)
    {
        using var database = OpenDatabase();
        RegisterTitleSearch(database);
        using var transaction = database.BeginTransaction(deferred: true);
        using var command = database.CreateCommand();
        command.Transaction = transaction;
        const string filter = """
             AND ($search = '' OR title_contains(m.title, $search) OR title_contains(COALESCE(m.alternate_title, ''), $search))
            """;
        command.Parameters.AddWithValue("$search", search);
        command.CommandText = "SELECT COUNT(*)" + AvailableAssociationSeries + filter;
        var total = (long)command.ExecuteScalar()!;
        command.CommandText = "SELECT m.id, m.title, m.alternate_title, m.kind, m.provider_id" + AvailableAssociationSeries +
            filter + " ORDER BY m.title COLLATE NOCASE, m.id LIMIT $limit OFFSET $offset;";
        command.Parameters.AddWithValue("$limit", limit);
        command.Parameters.AddWithValue("$offset", offset);
        var matches = new List<AssociationSeries>(limit);
        using var rows = command.ExecuteReader();
        while (rows.Read()) matches.Add(ReadAssociationSeries(rows));
        return new(total, matches);
    }

    public AssociationPreview? PreviewAssociation(AssociationScope scope)
    {
        using var database = OpenDatabase();
        using var transaction = database.BeginTransaction(deferred: true);
        using var command = AssociationCommand(database, transaction, scope);
        return ReadAssociation(command, scope)?.Preview;
    }

    public bool ApplyAssociation(AssociationScope scope, string revision)
    {
        using var database = OpenCoverDatabase();
        using var transaction = database.BeginTransaction(deferred: false);
        using var command = AssociationCommand(database, transaction, scope);
        command.CommandText = """
            SELECT EXISTS(SELECT 1 FROM library_root WHERE state IN ('queued', 'scanning'))
                OR EXISTS(SELECT 1 FROM file_operation WHERE content_id = $content AND state IN ('applying', 'restoring'));
            """;
        if ((long)command.ExecuteScalar()! != 0 ||
            ReadAssociation(command, scope) is not { } plan || plan.Preview.Revision != revision)
            return false;

        command.Parameters.AddWithValue("$target", plan.Key);
        command.Parameters.AddWithValue("$targetId", plan.Preview.Target.Id);
        command.Parameters.AddWithValue("$title", plan.Preview.Target.Title);
        command.CommandText = """
            INSERT INTO content_metadata(content_id, metadata_id) VALUES ($content, $targetId)
            ON CONFLICT (content_id) DO UPDATE SET metadata_id = excluded.metadata_id;
            INSERT OR IGNORE INTO file_association_review(file_id, content_id)
            SELECT id, content_id FROM library_file WHERE content_id = $content AND root_id IN (SELECT root_id FROM library_folder);
            DELETE FROM import_file_split WHERE content_id = $content;
            UPDATE library_file SET series_key = $target, series_title = $title
            WHERE content_id = $content AND root_id IN (SELECT root_id FROM library_folder);
            """;
        command.ExecuteNonQuery();

        var affectedRoot = command.Parameters.Add("$affectedRoot", SqliteType.Text);
        var affectedSeries = command.Parameters.Add("$affectedSeries", SqliteType.Text);
        foreach (var (root, series) in plan.Groups)
        {
            affectedRoot.Value = root;
            affectedSeries.Value = series;
            command.CommandText = """
                DELETE FROM library_series_cover AS cover WHERE root_id = $affectedRoot AND series_key = $affectedSeries
                    AND NOT EXISTS(SELECT 1 FROM library_file f WHERE f.root_id = cover.root_id AND f.series_key = cover.series_key
                        AND (cover.archive_entry IS NULL OR f.relative_path = cover.relative_path));
                """;
            command.ExecuteNonQuery();
            RefreshManagedArchiveCover(database, transaction, root, series);
        }
        foreach (var root in plan.Groups.Select(group => group.Root).Distinct())
            RefreshManagedArchiveCover(database, transaction, root, plan.Key);

        transaction.Commit();
        return true;
    }

    private sealed record AssociationPlan(AssociationPreview Preview, string Key, IReadOnlySet<(Guid Root, string Series)> Groups);

    private static AssociationPlan? ReadAssociation(SqliteCommand command, AssociationScope scope)
    {
        command.CommandText = """
            SELECT previous.id, previous.title FROM library_file f JOIN library_folder folder ON folder.root_id = f.root_id
            LEFT JOIN content_metadata cm ON cm.content_id = f.content_id LEFT JOIN series_metadata previous ON previous.id = cm.metadata_id
            WHERE f.root_id = $root AND f.id = $file AND f.content_id = $content AND f.series_key = $series;
            """;
        string? previousId;
        string? previousTitle;
        using (var rows = command.ExecuteReader())
        {
            if (!rows.Read()) return null;
            previousId = ReadText(rows, 0);
            previousTitle = ReadText(rows, 1);
        }

        command.CommandText = """
            SELECT m.id, m.title, m.alternate_title, m.kind, m.provider_id, m.series_key, m.updated_at
            """ + AvailableAssociationSeries + " AND m.id = COALESCE((SELECT series_id FROM series_identity_alias WHERE previous_id = $metadata), $metadata);";
        AssociationSeries target;
        string key;
        string updated;
        using (var rows = command.ExecuteReader())
        {
            if (!rows.Read()) return null;
            target = ReadAssociationSeries(rows);
            key = rows.GetString(5);
            updated = rows.GetString(6);
        }

        command.Parameters.AddWithValue("$targetKey", key);
        command.Parameters.AddWithValue("$chosenId", target.Id);
        command.CommandText = """
            WITH affected AS (
                SELECT DISTINCT f.root_id FROM library_file f JOIN library_folder folder ON folder.root_id = f.root_id
                WHERE f.content_id = $content
            )
            SELECT EXISTS(
                SELECT 1 FROM affected a JOIN series_metadata_binding b ON b.root_id = a.root_id
                WHERE b.series_key = $targetKey AND b.metadata_id <> $chosenId
                UNION ALL
                SELECT 1 FROM affected a JOIN library_file f ON f.root_id = a.root_id JOIN content_metadata cm ON cm.content_id = f.content_id
                WHERE f.series_key = $targetKey AND f.content_id <> $content AND cm.metadata_id <> $chosenId
            );
            """;
        if ((long)command.ExecuteScalar()! != 0) return null;

        using var fingerprint = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        fingerprint.AppendData(JsonSerializer.SerializeToUtf8Bytes(new { scope, previousId, previousTitle, key, updated }));
        command.CommandText = """
            SELECT f.root_id, f.id, f.series_key, f.relative_path, COALESCE(NULLIF(folder.name, ''), folder.path)
            FROM library_file f JOIN library_folder folder ON folder.root_id = f.root_id
            WHERE f.content_id = $content
            ORDER BY (f.root_id = $root AND f.id = $file) DESC, f.root_id, f.relative_path, f.id;
            """;
        var locations = new List<AssociationLocation>();
        var groups = new HashSet<(Guid, string)>();
        var count = 0;
        using var aliases = command.ExecuteReader();
        while (aliases.Read())
        {
            var root = aliases.GetGuid(0);
            var series = aliases.GetString(2);
            var location = new AssociationLocation(root, aliases.GetString(4), aliases.GetString(3));
            groups.Add((root, series));
            if (locations.Count < 8) locations.Add(location);
            count++;
            fingerprint.AppendData(JsonSerializer.SerializeToUtf8Bytes(new { location, File = aliases.GetString(1), series }));
        }

        return new(new(previousTitle, target, count, locations, Convert.ToHexStringLower(fingerprint.GetHashAndReset())), key, groups);
    }

    private static SqliteCommand AssociationCommand(SqliteConnection database, SqliteTransaction transaction, AssociationScope scope)
    {
        var command = database.CreateCommand();
        command.Transaction = transaction;
        command.Parameters.AddWithValue("$root", scope.Root);
        command.Parameters.AddWithValue("$file", scope.FileId);
        command.Parameters.AddWithValue("$content", scope.ContentId);
        command.Parameters.AddWithValue("$series", scope.SeriesKey);
        command.Parameters.AddWithValue("$metadata", scope.MetadataId);
        return command;
    }

    private static AssociationSeries ReadAssociationSeries(SqliteDataReader rows) =>
        new(rows.GetString(0), rows.GetString(1), ReadText(rows, 2), rows.GetString(3), ReadInt(rows, 4));
}
