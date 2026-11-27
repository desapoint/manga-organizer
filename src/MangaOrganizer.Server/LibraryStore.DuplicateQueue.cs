using Microsoft.Data.Sqlite;

namespace MangaOrganizer.Server;

internal sealed record DuplicateSeries(Guid RootId, string SeriesKey, string Title, long CandidateFiles);
internal sealed record DuplicateQueueFile(string FileId, string RelativePath, string[] Reasons);

internal sealed partial class LibraryStore
{
    private const string ScopedDuplicateCoverage = """
        WITH scoped AS (
            SELECT f.id, f.root_id, f.series_key, f.series_title, f.content_id, f.relative_path, f.volume_hint, f.chapter_hint
            FROM library_file f JOIN library_folder folder ON folder.root_id = f.root_id
            WHERE f.content_id IS NOT NULL AND ($root IS NULL OR f.root_id = $root)
                AND ($series IS NULL OR f.series_key = $series)
        ), chapter_labels AS (
            SELECT f.id, f.root_id, f.series_key, c.chapter, c.volume
            FROM scoped f JOIN content_chapter c ON c.content_id = f.content_id
            UNION
            SELECT f.id, f.root_id, f.series_key, c.value, inferred.volume
            FROM scoped f JOIN inferred_volume_chapters inferred ON inferred.content_id = f.content_id, json_each(inferred.chapters) c
            WHERE NOT EXISTS(SELECT 1 FROM content_chapter manual WHERE manual.content_id = f.content_id)
            UNION
            SELECT f.id, f.root_id, f.series_key, f.chapter_hint, COALESCE(f.volume_hint, '') FROM scoped f
            WHERE f.chapter_hint IS NOT NULL
                AND NOT EXISTS(SELECT 1 FROM content_chapter manual WHERE manual.content_id = f.content_id)
                AND NOT EXISTS(SELECT 1 FROM inferred_volume_chapters inferred WHERE inferred.content_id = f.content_id)
        )
        """;

    private const string DuplicateQueue = ScopedDuplicateCoverage + """
        , repeated_chapters AS (
            SELECT DISTINCT a.id FROM chapter_labels a JOIN chapter_labels b USING (root_id, series_key, chapter)
            WHERE a.id <> b.id AND (a.volume = b.volume OR a.volume = '' OR b.volume = '')
        ), repeated_volumes AS (
            SELECT root_id, series_key, volume_hint FROM scoped f
            WHERE volume_hint IS NOT NULL AND chapter_hint IS NULL
            GROUP BY root_id, series_key, volume_hint HAVING COUNT(*) > 1
                AND MAX(NOT EXISTS(SELECT 1 FROM chapter_labels label WHERE label.id = f.id))
        ), reasons AS (
            SELECT f.id, 'bytes' AS reason FROM scoped f WHERE EXISTS(
                SELECT 1 FROM library_file copy JOIN library_folder folder ON folder.root_id = copy.root_id
                WHERE copy.content_id = f.content_id AND copy.id <> f.id)
            UNION
            SELECT id, 'chapter' FROM repeated_chapters
            UNION
            SELECT f.id, 'volume' FROM scoped f JOIN repeated_volumes USING (root_id, series_key, volume_hint)
            WHERE f.chapter_hint IS NULL
        ), matching AS (
            SELECT DISTINCT id FROM reasons WHERE $kind IS NULL OR reason = $kind
        )
        """;

    public CatalogPage<DuplicateSeries> ReadDuplicateSeries(Guid? root, string? kind, string search, int offset, int limit)
    {
        using var database = OpenDatabase();
        RegisterTitleSearch(database);
        using var transaction = database.BeginTransaction(deferred: true);
        using var command = DuplicateQueueCommand(database, transaction, root, null, kind, offset, limit);
        command.Parameters.AddWithValue("$search", search);
        const string filter = """
            , grouped AS (
                SELECT f.root_id, f.series_key, MIN(COALESCE(m.title, f.series_title, f.relative_path)) AS title, COUNT(*) AS files
                FROM scoped f JOIN matching USING (id)
                LEFT JOIN series_metadata_binding binding ON binding.root_id = f.root_id AND binding.series_key = f.series_key
                LEFT JOIN content_metadata cm ON cm.content_id = f.content_id
                LEFT JOIN series_metadata m ON m.id = COALESCE(binding.metadata_id, cm.metadata_id)
                WHERE $search = '' OR title_contains(COALESCE(m.title, f.series_title, f.relative_path), $search)
                    OR title_contains(COALESCE(f.series_title, ''), $search)
                GROUP BY f.root_id, f.series_key
            )
            """;
        command.CommandText = DuplicateQueue + filter + " SELECT COUNT(*) FROM grouped;";
        var total = (long)command.ExecuteScalar()!;
        command.CommandText = DuplicateQueue + filter + """

            SELECT root_id, series_key, title, files FROM grouped
            ORDER BY title COLLATE NOCASE, root_id, series_key LIMIT $limit OFFSET $offset;
            """;
        var rows = new List<DuplicateSeries>(limit);
        using var result = command.ExecuteReader();
        while (result.Read()) rows.Add(new(result.GetGuid(0), result.GetString(1), result.GetString(2), result.GetInt64(3)));
        return new(total, rows);
    }

    public CatalogPage<DuplicateQueueFile> ReadDuplicateQueue(Guid root, string series, string? kind, int offset, int limit)
    {
        using var database = OpenDatabase();
        using var transaction = database.BeginTransaction(deferred: true);
        using var command = DuplicateQueueCommand(database, transaction, root, series, kind, offset, limit);
        command.CommandText = DuplicateQueue + " SELECT COUNT(*) FROM matching;";
        var total = (long)command.ExecuteScalar()!;
        command.CommandText = DuplicateQueue + """

            SELECT f.id, f.relative_path, (SELECT group_concat(reason) FROM reasons WHERE id = f.id)
            FROM scoped f JOIN matching USING (id)
            ORDER BY f.relative_path, f.id LIMIT $limit OFFSET $offset;
            """;
        var rows = new List<DuplicateQueueFile>(limit);
        using var result = command.ExecuteReader();
        while (result.Read()) rows.Add(new(result.GetString(0), result.GetString(1), result.GetString(2).Split(',')));
        return new(total, rows);
    }

    private static SqliteCommand DuplicateQueueCommand(SqliteConnection database, SqliteTransaction transaction, Guid? root,
        string? series, string? kind, int offset, int limit)
    {
        var command = database.CreateCommand();
        command.Transaction = transaction;
        command.Parameters.AddWithValue("$root", (object?)root ?? DBNull.Value);
        command.Parameters.AddWithValue("$series", (object?)series ?? DBNull.Value);
        command.Parameters.AddWithValue("$kind", (object?)kind ?? DBNull.Value);
        command.Parameters.AddWithValue("$offset", offset);
        command.Parameters.AddWithValue("$limit", limit);
        return command;
    }
}
