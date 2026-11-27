using System.Text.Json;

namespace MangaOrganizer.Server;

internal sealed partial class LibraryStore
{
    public string? GroupIntakeSeries(Guid root, string[] keys, string title, string? target, bool confirm, string kind, SeriesTag[]? tags)
    {
        using var database = OpenCoverDatabase();
        database.CreateFunction<string, string, string?>("selected_title_chapter", SeriesSuggestions.ChapterForSelectedTitle, isDeterministic: true);
        using var transaction = database.BeginTransaction(deferred: false);
        var groupKey = target ?? SeriesSuggestions.Key("manual/" + Guid.NewGuid().ToString("N"), null);
        using var command = GroupCommand(database, transaction, root, groupKey);
        command.Parameters.AddWithValue("$selected", JsonSerializer.Serialize(keys));
        command.CommandText = """
            SELECT COUNT(DISTINCT f.series_key),
                MAX(f.content_id IS NULL OR EXISTS (
                    SELECT 1 FROM file_operation op WHERE op.content_id = f.content_id AND op.state IN ('applying', 'restoring'))),
                MAX(cm.content_id IS NOT NULL AND cm.metadata_id IS NOT (
            """ + MetadataIdForGroup + """
                )),
                MAX(f.series_key <> $series AND binding.metadata_id IS NOT NULL)
            FROM library_file f
            JOIN library_folder folder ON folder.root_id = f.root_id
            JOIN library_root state ON state.id = f.root_id
            LEFT JOIN content_metadata cm ON cm.content_id = f.content_id
            LEFT JOIN series_metadata_binding binding ON binding.root_id = f.root_id AND binding.series_key = f.series_key
            WHERE f.root_id = $root AND f.series_key IN (SELECT value FROM json_each($selected))
              AND folder.kind IN ('import', 'both') AND state.state NOT IN ('scanning', 'queued', 'unavailable');
            """;
        using (var rows = command.ExecuteReader())
        {
            rows.Read();
            if (rows.GetInt32(0) != keys.Length || rows.GetBoolean(1))
                return "The selected groups changed or contain busy or unverified files. Refresh the inventory and review your selection.";
            if (rows.GetBoolean(2) || rows.GetBoolean(3))
                return "The selection includes a different saved association. Correct that association in Tools before grouping these entries.";
        }

        var existing = target is null ? null : ReadMetadata(command);
        if (target is not null && existing is null) return "The chosen series has no saved metadata. Refresh the inventory and try again.";
        var groupTitle = existing?.Title ?? title;
        command.Parameters.AddWithValue("$groupTitle", groupTitle);
        command.CommandText = """
            INSERT INTO import_file_split(file_id, content_id)
            SELECT f.id, f.content_id FROM library_file f
            WHERE f.root_id = $root AND f.series_key <> $series AND f.series_key IN (SELECT value FROM json_each($selected))
            ON CONFLICT(file_id) DO UPDATE SET content_id = excluded.content_id;
            UPDATE library_file SET series_key = $series, series_title = $groupTitle,
                chapter_hint = COALESCE(chapter_hint, CASE WHEN volume_hint IS NULL THEN selected_title_chapter(relative_path, $groupTitle) END)
            WHERE root_id = $root AND series_key <> $series AND series_key IN (SELECT value FROM json_each($selected));
            DELETE FROM library_series_cover WHERE root_id = $root AND series_key <> $series
                AND series_key IN (SELECT value FROM json_each($selected));
            """;
        command.ExecuteNonQuery();
        RefreshManagedArchiveCover(database, transaction, root, groupKey);

        if (confirm)
        {
            if (existing is null)
                WriteMetadata(command, new(groupTitle, kind, null, null, null, null, null, null, null, tags));
            else
                AttachSeriesMetadata(command);
        }
        else if (existing is null)
        {
            command.CommandText = """
                INSERT INTO metadata_suggestion(series_key, title, revision, queued, requested_at)
                VALUES ($series, $groupTitle, lower(hex(randomblob(16))), 1, unixepoch())
                ON CONFLICT(series_key) DO NOTHING;
                """;
            command.ExecuteNonQuery();
        }

        transaction.Commit();
        return null;
    }
}
