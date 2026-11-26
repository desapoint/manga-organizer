using Microsoft.Data.Sqlite;

namespace MangaOrganizer.Server;

internal sealed record ReleaseTags(string? Language, string Color, string Censorship, string? Edition, string? Group, int Revision);
internal sealed record VersionFile(Guid RootId, string FileId, string ContentId, string RelativePath, string Reason, ReleaseTags Tags);
internal sealed record VersionSeries(Guid RootId, string SeriesKey, string Title, long ReleaseCount);
internal sealed record StoredRelease(string ContentId, string RelativePath, ReleaseTags Tags);
internal sealed record SelectedRelease(string FileId, string ContentId, string RelativePath, ReleaseTags Tags);
internal sealed record VersionFilters(string Search, string? Language, string? Color, string? Censorship, string? Edition, string? Group, bool? Reviewed);

internal sealed partial class LibraryStore
{
    private const string ReleaseTagColumns = """
        tag.language, COALESCE(tag.color, 'unknown') AS color, COALESCE(tag.censorship, 'unknown') AS censorship,
        tag.edition_name, tag.group_name, COALESCE(tag.revision, 0) AS revision
        """;

    private const string VersionCandidates = ScopedDuplicateCoverage + """
        , spans AS (
            SELECT id, json_group_array(json_array(volume, chapter) ORDER BY volume, chapter) AS span
            FROM chapter_labels GROUP BY id
        ), same_span AS (
            SELECT f.root_id, f.series_key, s.span FROM scoped f JOIN spans s USING (id)
            GROUP BY f.root_id, f.series_key, s.span HAVING COUNT(DISTINCT f.content_id) > 1
        ), same_volume AS (
            SELECT f.root_id, f.series_key, f.volume_hint FROM scoped f LEFT JOIN spans s USING (id)
            WHERE f.volume_hint IS NOT NULL AND f.chapter_hint IS NULL
            GROUP BY f.root_id, f.series_key, f.volume_hint
            HAVING COUNT(DISTINCT f.content_id) > 1 AND MAX(s.span IS NULL)
        ), candidates AS (
            SELECT f.*,
                CASE WHEN matched.span IS NOT NULL THEN 'span' ELSE 'volume' END AS reason,
                ROW_NUMBER() OVER (PARTITION BY f.root_id, f.series_key, f.content_id
                    ORDER BY matched.span IS NULL, f.relative_path, f.id) AS representative
            FROM scoped f LEFT JOIN spans s USING (id)
            LEFT JOIN same_span matched ON matched.root_id = f.root_id AND matched.series_key = f.series_key AND matched.span = s.span
            LEFT JOIN same_volume volume ON volume.root_id = f.root_id AND volume.series_key = f.series_key AND volume.volume_hint = f.volume_hint
            WHERE matched.span IS NOT NULL OR volume.volume_hint IS NOT NULL AND f.chapter_hint IS NULL
        ), matched AS (
            SELECT f.*, COALESCE(m.title, f.series_title, f.relative_path) AS title,
        """ + " " + ReleaseTagColumns + """

            FROM candidates f
            LEFT JOIN release_tag tag ON tag.content_id = f.content_id
            LEFT JOIN series_metadata_binding binding ON binding.root_id = f.root_id AND binding.series_key = f.series_key
            LEFT JOIN content_metadata link ON link.content_id = f.content_id
            LEFT JOIN series_metadata m ON m.id = COALESCE(binding.metadata_id, link.metadata_id)
            WHERE f.representative = 1
                AND ($search = '' OR title_contains(COALESCE(m.title, f.series_title, f.relative_path), $search)
                    OR title_contains(f.relative_path, $search))
                AND ($language IS NULL OR tag.language = $language)
                AND ($color IS NULL OR COALESCE(tag.color, 'unknown') = $color)
                AND ($censorship IS NULL OR COALESCE(tag.censorship, 'unknown') = $censorship)
                AND ($edition IS NULL OR title_contains(COALESCE(tag.edition_name, ''), $edition))
                AND ($group IS NULL OR title_contains(COALESCE(tag.group_name, ''), $group))
                AND ($reviewed IS NULL OR (tag.revision IS NOT NULL) = $reviewed)
        )
        """;

    public CatalogPage<VersionSeries> ReadVersionSeries(Guid? root, VersionFilters filters, int offset, int limit) =>
        ReadVersions(root, null, filters, offset, limit, """
             , grouped AS (
                SELECT root_id, series_key, MIN(title) AS title, COUNT(*) AS releases
                FROM matched GROUP BY root_id, series_key
             )
            """, "grouped", "root_id, series_key, title, releases", "title COLLATE NOCASE, root_id, series_key",
            row => new VersionSeries(row.GetGuid(0), row.GetString(1), row.GetString(2), row.GetInt64(3)));

    public CatalogPage<VersionFile> ReadVersionFiles(Guid root, string series, VersionFilters filters, int offset, int limit) =>
        ReadVersions(root, series, filters, offset, limit, "", "matched",
            "root_id, id, content_id, relative_path, reason, language, color, censorship, edition_name, group_name, revision", "relative_path, id",
            row => new VersionFile(row.GetGuid(0), row.GetString(1), row.GetString(2), row.GetString(3), row.GetString(4), ReadTags(row, 5)));

    private CatalogPage<T> ReadVersions<T>(Guid? root, string? series, VersionFilters filters, int offset, int limit,
        string group, string table, string columns, string sort, Func<SqliteDataReader, T> materialize)
    {
        using var database = OpenDatabase();
        RegisterTitleSearch(database);
        using var transaction = database.BeginTransaction(deferred: true);
        using var command = database.CreateCommand();
        command.Transaction = transaction;
        command.Parameters.AddWithValue("$root", (object?)root ?? DBNull.Value);
        command.Parameters.AddWithValue("$series", (object?)series ?? DBNull.Value);
        command.Parameters.AddWithValue("$search", filters.Search);
        command.Parameters.AddWithValue("$reviewed", (object?)filters.Reviewed ?? DBNull.Value);
        BindTagValues(command, filters.Language, filters.Color, filters.Censorship, filters.Edition, filters.Group);
        command.CommandText = VersionCandidates + group + " SELECT COUNT(*) FROM " + table;
        var total = (long)command.ExecuteScalar()!;
        command.CommandText = VersionCandidates + group + " SELECT " + columns + " FROM " + table
            + " ORDER BY " + sort + " LIMIT $limit OFFSET $offset;";
        command.Parameters.AddWithValue("$limit", limit);
        command.Parameters.AddWithValue("$offset", offset);
        using var rows = command.ExecuteReader();
        var result = new List<T>(limit);
        while (rows.Read()) result.Add(materialize(rows));
        return new(total, result);
    }

    public StoredRelease? ReadStoredRelease(Guid root, string fileId)
    {
        using var database = OpenDatabase();
        using var command = database.CreateCommand();
        BindReleaseLocation(command, root, fileId);
        return ReadStoredRelease(command);
    }

    public bool SaveReleaseTags(Guid root, string fileId, string contentId, ReleaseTags tags)
    {
        using var database = OpenDatabase();
        using var transaction = database.BeginTransaction(deferred: false);
        using var command = database.CreateCommand();
        command.Transaction = transaction;
        BindReleaseLocation(command, root, fileId);
        if (ReadStoredRelease(command) is not { } current || current.ContentId != contentId || current.Tags.Revision != tags.Revision)
            return false;

        WriteReleaseTags(database, transaction, contentId, tags);
        transaction.Commit();
        return true;
    }

    public IReadOnlyList<SelectedRelease>? ReadReleaseTagsBatch(Guid root, IReadOnlyList<string> fileIds)
    {
        using var database = OpenDatabase();
        using var transaction = database.BeginTransaction(deferred: true);
        using var command = database.CreateCommand();
        command.Transaction = transaction;
        BindReleaseLocation(command, root, "");
        var files = new List<SelectedRelease>(fileIds.Count);
        foreach (var fileId in fileIds)
        {
            command.Parameters["$file"].Value = fileId;
            if (ReadStoredRelease(command) is not { } release)
                return null;
            files.Add(new(fileId, release.ContentId, release.RelativePath, release.Tags));
        }
        return files;
    }

    public bool SaveReleaseTagsBatch(Guid root, IReadOnlyList<ReleaseTagTarget> targets, string field, string? value)
    {
        using var database = OpenDatabase();
        using var transaction = database.BeginTransaction(deferred: false);
        using var command = database.CreateCommand();
        command.Transaction = transaction;
        BindReleaseLocation(command, root, "");
        var tagsByContent = new Dictionary<string, ReleaseTags>();
        foreach (var target in targets)
        {
            command.Parameters["$file"].Value = target.FileId;
            if (ReadStoredRelease(command) is not { } current || current.ContentId != target.ContentId || current.RelativePath != target.RelativePath
                || current.Tags.Revision != target.Revision)
                return false;
            tagsByContent.TryAdd(current.ContentId, current.Tags);
        }

        foreach (var (contentId, current) in tagsByContent)
        {
            var updated = field switch
            {
                "language" => current with { Language = value?.ToLowerInvariant() },
                "color" => current with { Color = value ?? "unknown" },
                "censorship" => current with { Censorship = value ?? "unknown" },
                "edition" => current with { Edition = value },
                "group" => current with { Group = value },
                _ => throw new ArgumentOutOfRangeException(nameof(field)),
            };
            WriteReleaseTags(database, transaction, contentId, updated);
        }
        transaction.Commit();
        return true;
    }

    private static void WriteReleaseTags(SqliteConnection database, SqliteTransaction transaction, string contentId, ReleaseTags tags)
    {
        using var command = database.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO release_tag(content_id, language, color, censorship, edition_name, group_name, revision, updated_at)
            VALUES ($content, $language, $color, $censorship, $edition, $group, 1, $now)
            ON CONFLICT(content_id) DO UPDATE SET language = excluded.language, color = excluded.color, censorship = excluded.censorship,
                edition_name = excluded.edition_name, group_name = excluded.group_name,
                revision = release_tag.revision + 1, updated_at = excluded.updated_at;
            """;
        BindTagValues(command, tags.Language, tags.Color, tags.Censorship, tags.Edition, tags.Group);
        command.Parameters.AddWithValue("$content", contentId);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow);
        command.ExecuteNonQuery();
    }

    private static StoredRelease? ReadStoredRelease(SqliteCommand command)
    {
        command.CommandText = "SELECT f.content_id, f.relative_path, " + ReleaseTagColumns + """

            FROM library_file f JOIN library_folder folder ON folder.root_id = f.root_id
            LEFT JOIN release_tag tag ON tag.content_id = f.content_id
            WHERE f.root_id = $root AND f.id = $file AND f.content_id IS NOT NULL;
            """;
        using var rows = command.ExecuteReader();
        return rows.Read() ? new(rows.GetString(0), rows.GetString(1), ReadTags(rows, 2)) : null;
    }

    private static void BindReleaseLocation(SqliteCommand command, Guid root, string fileId)
    {
        command.Parameters.AddWithValue("$root", root);
        command.Parameters.AddWithValue("$file", fileId);
    }

    private static void BindTagValues(SqliteCommand command, string? language, string? color, string? censorship, string? edition, string? group)
    {
        command.Parameters.AddWithValue("$language", (object?)language ?? DBNull.Value);
        command.Parameters.AddWithValue("$color", (object?)color ?? DBNull.Value);
        command.Parameters.AddWithValue("$censorship", (object?)censorship ?? DBNull.Value);
        command.Parameters.AddWithValue("$edition", (object?)edition ?? DBNull.Value);
        command.Parameters.AddWithValue("$group", (object?)group ?? DBNull.Value);
    }

    private static ReleaseTags ReadTags(SqliteDataReader row, int offset) => new(ReadText(row, offset), row.GetString(offset + 1),
        row.GetString(offset + 2), ReadText(row, offset + 3), ReadText(row, offset + 4), row.GetInt32(offset + 5));
}
