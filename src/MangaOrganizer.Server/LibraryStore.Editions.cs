using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace MangaOrganizer.Server;

internal sealed record ChapterRelease(string ContentId, string RelativePath, string Format, int? PageIndex, int? PageCount, bool UnresolvedVolume = false);
internal sealed record EditionVolume(string Id, string Edition, string Volume, string[] Chapters, string[] ContentIds,
    string[] Available, string State);
internal sealed record EditionVolumeChange(string? Edition, string? Volume, string[]? Chapters, string[]? ContentIds, string? FullVolumeContentId);

internal sealed partial class LibraryStore
{
    private static void AddWorkKey(SqliteCommand command, Guid root, string seriesKey)
    {
        command.CommandText = "SELECT " + MetadataIdForGroup;
        var metadataId = command.ExecuteScalar() as string;
        var localKey = $"{root:D}/{seriesKey}";
        command.Parameters.AddWithValue("$localWork", localKey);
        command.Parameters.AddWithValue("$work", metadataId ?? localKey);
    }

    public IReadOnlyList<ChapterRelease>? ReadChapterReleases(Guid root, string series, string volume, string chapter, Guid owner)
    {
        using var database = OpenDatabase();
        using var transaction = database.BeginTransaction(deferred: true);
        using var command = GroupCommand(database, transaction, root, series);
        if (!GroupExists(command)) return null;

        command.Parameters.AddWithValue("$volume", volume);
        command.Parameters.AddWithValue("$chapter", chapter);
        command.Parameters.AddWithValue("$owner", owner);
        command.CommandText = "WITH covered AS (" + GroupCoverage + """
            )
            SELECT f.content_id, MIN(f.relative_path), f.extension, p.page_index, p.page_count
            FROM covered c JOIN library_file f ON f.content_id = c.content_id
            JOIN library_root r ON r.id = f.root_id
            LEFT JOIN reading_progress p ON p.content_id = f.content_id AND p.owner_id = $owner
            WHERE c.volume = $volume AND c.chapter = $chapter AND f.root_id = $root AND f.series_key = $series
                AND r.state <> 'unavailable' AND f.extension IN ('cbz', 'zip', 'cbr', 'rar')
            GROUP BY f.content_id ORDER BY MIN(f.relative_path);
            """;
        return ReadReleases(command);
    }

    public bool SaveChapterCompletion(Guid root, string series, string volume, string chapter, Guid owner, bool completed)
    {
        using var database = OpenDatabase();
        using var transaction = database.BeginTransaction(deferred: false);
        using var command = GroupCommand(database, transaction, root, series);
        if (!GroupExists(command)) return false;

        AddWorkKey(command, root, series);
        command.Parameters.AddWithValue("$volume", volume);
        command.Parameters.AddWithValue("$chapter", chapter);
        command.Parameters.AddWithValue("$owner", owner);
        command.Parameters.AddWithValue("$completed", completed);
        command.CommandText = "WITH covered AS (" + GroupCoverage + """
            )
            INSERT INTO chapter_completion(owner_id, work_key, volume, chapter, completed)
            SELECT $owner, $work, $volume, $chapter, $completed
            WHERE EXISTS(SELECT 1 FROM covered WHERE volume = $volume AND chapter = $chapter)
            ON CONFLICT(owner_id, work_key, volume, chapter) DO UPDATE SET completed = excluded.completed;
            """;
        var changed = command.ExecuteNonQuery() == 1;
        transaction.Commit();
        return changed;
    }

    public IReadOnlyList<EditionVolume>? ReadEditionVolumes(Guid root, string series)
    {
        using var database = OpenDatabase();
        using var transaction = database.BeginTransaction(deferred: true);
        using var command = GroupCommand(database, transaction, root, series);
        if (!GroupExists(command)) return null;

        AddWorkKey(command, root, series);
        command.CommandText = "WITH covered AS (" + GroupCoverage + """
            )
            SELECT m.id, m.edition, m.volume, m.chapters,
                (SELECT json_group_array(content_id) FROM edition_volume_release e WHERE e.manifest_id = m.id),
                (SELECT json_group_array(chapter) FROM (
                    SELECT DISTINCT c.chapter FROM covered c
                    JOIN edition_volume_release e ON e.content_id = c.content_id AND e.manifest_id = m.id
                    WHERE c.volume = m.volume)),
                EXISTS(SELECT 1 FROM edition_volume_release e JOIN library_file f ON f.content_id = e.content_id
                    WHERE e.manifest_id = m.id AND f.root_id = $root AND f.series_key = $series
                        AND NOT EXISTS(SELECT 1 FROM covered c WHERE c.content_id = e.content_id AND c.volume = m.volume))
            FROM edition_volume_manifest m WHERE m.work_key = $work OR m.work_key = $localWork
            ORDER BY CAST(m.volume AS REAL), m.edition, m.id;
            """;
        using var rows = command.ExecuteReader();
        var volumes = new List<EditionVolume>();
        while (rows.Read())
        {
            var chapters = JsonSerializer.Deserialize<string[]>(rows.GetString(3))!;
            var ids = JsonSerializer.Deserialize<string[]>(rows.GetString(4))!;
            var available = JsonSerializer.Deserialize<string[]>(rows.GetString(5))!.Intersect(chapters, StringComparer.Ordinal)
                .OrderBy(Numeric).ToArray();
            var state = available.Length == chapters.Length ? "complete"
                : ids.Length == 0 || rows.GetBoolean(6) ? "unknown" : available.Length == 0 ? "missing" : "partial";
            volumes.Add(new(rows.GetString(0), rows.GetString(1), rows.GetString(2), chapters, ids, available, state));
        }
        return volumes;
    }

    public IReadOnlyList<ChapterRelease>? ReadEditionCandidates(Guid root, string series, string volume)
    {
        using var database = OpenDatabase();
        using var transaction = database.BeginTransaction(deferred: true);
        using var command = GroupCommand(database, transaction, root, series);
        if (!GroupExists(command)) return null;

        AddWorkKey(command, root, series);
        command.Parameters.AddWithValue("$volume", volume);
        command.CommandText = "WITH covered AS (" + GroupCoverage + """
            )
            SELECT f.content_id, MIN(f.relative_path), f.extension, NULL, NULL,
                MAX(f.volume_hint = $volume AND f.chapter_hint IS NULL
                    AND NOT EXISTS(SELECT 1 FROM covered c WHERE c.content_id = f.content_id))
            FROM library_file f
            WHERE f.root_id = $root AND f.series_key = $series AND f.content_id IS NOT NULL
              AND (f.volume_hint = $volume OR EXISTS(SELECT 1 FROM covered c WHERE c.volume = $volume AND c.content_id = f.content_id)
                OR EXISTS(SELECT 1 FROM edition_volume_release e JOIN edition_volume_manifest m ON m.id = e.manifest_id
                    WHERE (m.work_key = $work OR m.work_key = $localWork) AND m.volume = $volume AND e.content_id = f.content_id))
            GROUP BY f.content_id ORDER BY MIN(f.relative_path);
            """;
        return ReadReleases(command);
    }

    private static IReadOnlyList<ChapterRelease> ReadReleases(SqliteCommand command)
    {
        using var rows = command.ExecuteReader();
        var releases = new List<ChapterRelease>();
        while (rows.Read()) releases.Add(new(rows.GetString(0), rows.GetString(1), rows.GetString(2), ReadInt(rows, 3), ReadInt(rows, 4),
            rows.FieldCount > 5 && !rows.IsDBNull(5) && rows.GetBoolean(5)));
        return releases;
    }

    public bool SaveEditionManifest(Guid root, string series, string edition, string volume, string[] chapters,
        string[] contentIds, string? fullVolume)
    {
        using var database = OpenDatabase();
        using var transaction = database.BeginTransaction(deferred: false);
        using var command = GroupCommand(database, transaction, root, series);
        if (!GroupExists(command)) return false;

        AddWorkKey(command, root, series);
        command.Parameters.AddWithValue("$volume", volume);
        command.Parameters.AddWithValue("$edition", edition);
        using var membership = GroupCommand(database, transaction, root, series);
        var content = membership.Parameters.Add("$content", SqliteType.Text);
        foreach (var item in contentIds)
        {
            content.Value = item;
            if (!ContentInGroup(membership)) return false;
        }

        if (fullVolume is not null)
        {
            command.Parameters.AddWithValue("$full", fullVolume);
            command.CommandText = "WITH covered AS (" + GroupCoverage + """
                )
                SELECT 1 FROM library_file f WHERE f.root_id = $root AND f.series_key = $series AND f.content_id = $full
                    AND f.volume_hint = $volume AND f.chapter_hint IS NULL
                    AND NOT EXISTS(SELECT 1 FROM covered c WHERE c.content_id = $full)
                LIMIT 1;
                """;
            if (command.ExecuteScalar() is null) return false;
            command.CommandText = "INSERT INTO content_chapter(content_id, volume, chapter) SELECT $full, $volume, value FROM json_each($chapters);";
            command.Parameters.AddWithValue("$chapters", JsonSerializer.Serialize(chapters));
            command.ExecuteNonQuery();
        }
        else command.Parameters.AddWithValue("$chapters", JsonSerializer.Serialize(chapters));

        command.CommandText = """
            INSERT INTO edition_volume_manifest(id, work_key, edition, volume, chapters)
            VALUES($newId, $work, $edition, $volume, $chapters)
            ON CONFLICT(work_key, edition, volume) DO UPDATE SET chapters = excluded.chapters
            RETURNING id;
            """;
        command.Parameters.AddWithValue("$newId", Guid.NewGuid().ToString("N"));
        var id = (string)command.ExecuteScalar()!;
        command.Parameters.AddWithValue("$manifest", id);
        command.Parameters.AddWithValue("$releases", JsonSerializer.Serialize(contentIds));
        command.CommandText = """
            DELETE FROM edition_volume_release WHERE manifest_id = $manifest;
            INSERT INTO edition_volume_release(manifest_id, content_id) SELECT $manifest, value FROM json_each($releases);
            """;
        command.ExecuteNonQuery();
        transaction.Commit();
        return true;
    }
}
