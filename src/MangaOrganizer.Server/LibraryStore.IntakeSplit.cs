using Microsoft.Data.Sqlite;

namespace MangaOrganizer.Server;

internal sealed partial class LibraryStore
{
    public string? SplitIntakeFile(Guid root, string seriesKey, string fileId, string contentId)
    {
        using var database = OpenCoverDatabase();
        using var transaction = database.BeginTransaction(deferred: false);
        using var command = GroupCommand(database, transaction, root, seriesKey);
        command.Parameters.AddWithValue("$file", fileId);
        command.Parameters.AddWithValue("$content", contentId);
        command.CommandText = """
            SELECT f.relative_path, folder.path, cm.metadata_id IS NOT NULL,
                EXISTS(SELECT 1 FROM library_file alias WHERE alias.content_id = f.content_id AND alias.id <> f.id),
                EXISTS(SELECT 1 FROM library_file peer WHERE peer.root_id = f.root_id AND peer.series_key = f.series_key AND peer.id <> f.id)
            FROM library_file f JOIN library_folder folder ON folder.root_id = f.root_id
            JOIN library_root state ON state.id = f.root_id
            LEFT JOIN content_metadata cm ON cm.content_id = f.content_id
            WHERE f.root_id = $root AND f.id = $file AND f.series_key = $series AND f.content_id = $content
                AND folder.kind IN ('import', 'both') AND state.state NOT IN ('queued', 'scanning', 'unavailable')
                AND NOT EXISTS(SELECT 1 FROM file_operation op WHERE op.content_id = $content AND op.state IN ('applying', 'restoring'));
            """;
        string path;
        string rootPath;
        using (var rows = command.ExecuteReader())
        {
            if (!rows.Read()) return "The file changed or its folder is busy. Refresh the inventory and try again.";
            if (rows.GetBoolean(2) && rows.GetBoolean(3))
                return "This file shares a confirmed association with an exact copy. Review the copies or change their shared association in Tools.";
            if (!rows.GetBoolean(2) && !rows.GetBoolean(4)) return "This file already has its own group.";
            path = rows.GetString(0);
            rootPath = rows.GetString(1);
        }

        var title = SeriesSuggestions.CandidateTitle(path, rootPath) ?? Path.GetFileNameWithoutExtension(path);
        var target = SeriesSuggestions.Key("split/" + fileId, null);
        if (target == seriesKey) return "This file already has its own group.";
        command.Parameters.AddWithValue("$target", target);
        command.Parameters.AddWithValue("$title", title);
        command.CommandText = """
            DELETE FROM content_metadata WHERE content_id = $content;
            INSERT INTO import_file_split(file_id, content_id) VALUES ($file, $content)
                ON CONFLICT(file_id) DO UPDATE SET content_id = excluded.content_id;
            UPDATE library_file SET series_key = $target, series_title = $title WHERE id = $file;
            DELETE FROM library_series_cover AS cover WHERE root_id = $root AND series_key = $series
                AND NOT EXISTS(SELECT 1 FROM library_file f WHERE f.root_id = cover.root_id AND f.series_key = cover.series_key
                    AND (cover.archive_entry IS NULL OR cover.relative_path = f.relative_path));
            INSERT INTO metadata_suggestion(series_key, title, revision, queued, requested_at)
                VALUES ($target, $title, lower(hex(randomblob(16))), 1, unixepoch())
                ON CONFLICT(series_key) DO NOTHING;
            """;
        command.ExecuteNonQuery();
        RefreshManagedArchiveCover(database, transaction, root, seriesKey);
        RefreshManagedArchiveCover(database, transaction, root, target);
        transaction.Commit();
        return null;
    }

    private static void StageIntakeSplits(SqliteConnection database, Guid root)
    {
        using var command = database.CreateCommand();
        command.CommandText = """
            UPDATE scan_file AS f SET series_key = previous.series_key, series_title = previous.series_title
            FROM scan_hash h JOIN library_content content ON content.sha256 = h.sha256
            JOIN import_file_split split ON split.content_id = content.id
            JOIN library_file previous ON previous.id = split.file_id AND previous.root_id = $root
            WHERE f.relative_path = h.relative_path AND f.relative_path = previous.relative_path;
            """;
        command.Parameters.AddWithValue("$root", root);
        command.ExecuteNonQuery();
    }
}
