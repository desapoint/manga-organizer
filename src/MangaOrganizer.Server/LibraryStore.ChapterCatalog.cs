using Microsoft.Data.Sqlite;

namespace MangaOrganizer.Server;

internal sealed partial class LibraryStore
{
    private const string VerifiedChapterLabels = """
        SELECT DISTINCT manifest.volume, chapter.value AS chapter
        FROM edition_volume_manifest manifest, json_each(manifest.chapters) chapter
        WHERE manifest.work_key IN (
        """ + " " + GroupWorkKeys + ")";

    private static string ChapterCatalog(bool published) => "WITH RECURSIVE coverage AS MATERIALIZED (" + ScopedGroupCoverage(published)
        + "), readable_files AS (" + ReadableGroupFiles + SeriesVisibility(published) + $$"""
        ), readable AS (SELECT DISTINCT content_id FROM readable_files),
        verified AS (
            {{VerifiedChapterLabels}}
        ), identified AS MATERIALIZED (
            SELECT volume, chapter FROM coverage
            UNION
            SELECT volume, chapter FROM verified
        ), numbers(value) AS (
            SELECT 1 WHERE $released >= 1
            UNION ALL SELECT value + 1 FROM numbers WHERE value < $released
        ), labels AS MATERIALIZED (
            SELECT volume, chapter FROM identified
            UNION ALL
            SELECT '', CAST(n.value AS TEXT) FROM numbers n
            WHERE NOT EXISTS (SELECT 1 FROM identified i WHERE i.chapter = CAST(n.value AS TEXT))
        ), numbering AS (SELECT chapter, COUNT(*) AS occurrences FROM labels GROUP BY chapter),
        catalog AS (
            SELECT label.volume, label.chapter, COUNT(readable.content_id) AS readable_count, MIN(readable.content_id) AS content_id,
                COUNT(c.content_id) = 0 AS missing,
                COALESCE(status.completed, local.completed, unnumbered.completed, old_number.completed, (
                    SELECT MAX(related.completed) FROM chapter_completion related
                    WHERE related.owner_id = $owner AND related.work_key IN (
            {{GroupWorkKeys}}
                    ) AND related.chapter = label.chapter
                        AND (related.volume = label.volume OR n.occurrences = 1 AND related.volume = '')
                ), MAX(COALESCE(p.completed, 0))) AS completed,
                MAX(CASE WHEN readable.content_id = COALESCE(pref.content_id, local_pref.content_id, (
                    SELECT MIN(other.content_id) FROM preferred_chapter_release other
                    WHERE other.owner_id = $owner AND other.work_key IN (
            {{GroupWorkKeys}}
                    ) AND other.volume = label.volume AND other.chapter = label.chapter
                )) THEN readable.content_id END) AS preferred
            FROM labels label JOIN numbering n ON n.chapter = label.chapter
            LEFT JOIN coverage c ON c.volume = label.volume AND c.chapter = label.chapter
            LEFT JOIN readable ON readable.content_id = c.content_id
            LEFT JOIN reading_progress p ON p.owner_id = $owner AND p.content_id = c.content_id
            LEFT JOIN chapter_completion status ON status.owner_id = $owner AND status.work_key = $work
                AND status.volume = label.volume AND status.chapter = label.chapter
            LEFT JOIN chapter_completion local ON local.owner_id = $owner AND local.work_key = $localWork
                AND local.volume = label.volume AND local.chapter = label.chapter
            LEFT JOIN chapter_completion unnumbered ON unnumbered.owner_id = $owner AND unnumbered.work_key = $work
                AND label.volume <> '' AND n.occurrences = 1 AND unnumbered.volume = '' AND unnumbered.chapter = label.chapter
            LEFT JOIN chapter_completion old_number ON old_number.owner_id = $owner AND old_number.work_key = $localWork
                AND label.volume <> '' AND n.occurrences = 1 AND old_number.volume = '' AND old_number.chapter = label.chapter
            LEFT JOIN preferred_chapter_release pref ON pref.owner_id = $owner AND pref.work_key = $work
                AND pref.volume = label.volume AND pref.chapter = label.chapter
            LEFT JOIN preferred_chapter_release local_pref ON local_pref.owner_id = $owner AND local_pref.work_key = $localWork
                AND local_pref.volume = label.volume AND local_pref.chapter = label.chapter
            GROUP BY label.volume, label.chapter
        )
        """;

    public CatalogPage<LibraryChapter>? ReadChapterCatalog(Guid root, string series, Guid owner, bool published, string? volume,
        string search, int offset, int limit)
    {
        using var database = OpenDatabase();
        using var transaction = database.BeginTransaction(deferred: true);
        using var command = GroupCommand(database, transaction, root, series, published);
        if (!GroupExists(command, published)) return null;

        var metadata = ReadMetadata(command);
        AddWorkKey(command, root);
        command.Parameters.AddWithValue("$owner", owner);
        command.Parameters.AddWithValue("$released", metadata?.TotalChapters ?? 0);
        command.Parameters.AddWithValue("$filterVolume", (object?)volume ?? DBNull.Value);
        command.Parameters.AddWithValue("$search", search);
        command.Parameters.AddWithValue("$offset", offset);
        command.Parameters.AddWithValue("$limit", limit);
        const string filter = """
             WHERE ($filterVolume IS NULL OR volume = $filterVolume)
                AND ($search = '' OR instr(chapter, $search) > 0 OR instr(volume, $search) > 0)
            """;
        var catalog = ChapterCatalog(published);
        command.CommandText = catalog + " SELECT COUNT(*) FROM catalog" + filter;
        var total = (long)command.ExecuteScalar()!;
        command.CommandText = catalog + " SELECT volume, chapter, readable_count, content_id, missing, completed, preferred FROM catalog"
            + filter + " ORDER BY CAST(chapter AS REAL), CAST(volume AS REAL), volume, chapter LIMIT $limit OFFSET $offset;";
        var items = new List<LibraryChapter>(limit);
        using var rows = command.ExecuteReader();
        while (rows.Read())
        {
            var label = rows.GetString(0);
            var readableCount = rows.GetInt32(2);
            items.Add(new(label.Length == 0 ? null : label, rows.GetString(1), readableCount == 1 ? rows.GetString(3) : null,
                readableCount, rows.GetBoolean(5), ReadText(rows, 6), rows.GetBoolean(4)));
        }
        return new(total, items);
    }
}
