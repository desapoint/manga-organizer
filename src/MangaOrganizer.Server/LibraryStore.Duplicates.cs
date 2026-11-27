using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace MangaOrganizer.Server;

internal sealed record DuplicateFile(Guid RootId, string FileId, string RootPath, string RelativePath, string SeriesKey,
    string? SeriesTitle, string ContentId, string Format, long Size, DateTimeOffset ModifiedAt, byte[] Sha256, string? VolumeHint,
    string? ChapterHint, bool Managed, string State, bool HasCover, string? SeriesCoverKey, IReadOnlyList<SeriesChapter> Coverage);
internal sealed record SeriesDuplicateFile(Guid RootId, string FileId, string RelativePath, string FullPath, string Format, long Size,
    string? Sha256, string? Volume, bool VolumeArchive, bool HasCover, string? SeriesCoverKey, List<SeriesChapter> Chapters);
internal sealed record SeriesDuplicateReview(bool CanReviewQuarantine, IReadOnlyList<SeriesDuplicateFile> Files,
    IReadOnlyList<SeriesChapter> Expected);

internal sealed partial class LibraryStore
{
    public SeriesDuplicateReview? ReadSeriesDuplicateReview(Guid root, string series, bool compareFolders = false)
    {
        using var database = OpenCoverDatabase();
        using var transaction = database.BeginTransaction(deferred: true);
        using var command = GroupCommand(database, transaction, root, series);
        if (compareFolders && !GroupExists(command)) return null;
        command.Parameters.AddWithValue("$compareFolders", compareFolders);
        command.CommandText = """
            WITH scoped AS (
                SELECT f.* FROM library_file_numbered f
                WHERE f.series_key = $series AND (f.root_id = $root OR $compareFolders)
            ), identities AS (
                SELECT DISTINCT content_id FROM scoped
            ), mapped AS (
                SELECT c.content_id, c.volume, c.chapter, 'manual' AS source
                FROM content_chapter c JOIN identities i ON i.content_id = c.content_id
                UNION ALL
                SELECT v.content_id, v.volume, chapter.value, v.source
                FROM active_inferred_volume_chapters v JOIN identities i ON i.content_id = v.content_id, json_each(v.chapters) chapter
                WHERE NOT EXISTS(SELECT 1 FROM content_chapter c WHERE c.content_id = v.content_id)
            )
            SELECT f.id, f.relative_path, folder.path, f.extension, f.size, c.sha256, f.volume_hint, f.chapter_hint,
                folder.managed AND r.state = 'complete',
                EXISTS(SELECT 1 FROM artwork.archive_cover art WHERE art.archive_sha = c.sha256),
                (SELECT cover.series_key FROM library_series_cover cover WHERE cover.root_id = f.root_id AND cover.series_key = f.series_key),
                mapped.volume, mapped.chapter, mapped.source, f.root_id
            FROM scoped f JOIN library_folder folder ON folder.root_id = f.root_id
            JOIN library_root r ON r.id = f.root_id LEFT JOIN library_content c ON c.id = f.content_id
            LEFT JOIN mapped ON mapped.content_id = f.content_id
            ORDER BY f.root_id, f.relative_path, mapped.volume, mapped.chapter;
            """;
        var files = new Dictionary<string, SeriesDuplicateFile>(StringComparer.Ordinal);
        var managed = false;
        using (var rows = command.ExecuteReader())
        {
            while (rows.Read())
            {
                var id = rows.GetString(0);
                var volume = ReadText(rows, 6);
                var chapter = ReadText(rows, 7);
                if (!files.TryGetValue(id, out var file))
                {
                    var path = rows.GetString(1);
                    var locationRoot = rows.GetGuid(14);
                    file = new(locationRoot, id, path, Path.Combine(rows.GetString(2), path), rows.GetString(3), rows.GetInt64(4),
                        rows.IsDBNull(5) ? null : Convert.ToHexStringLower(rows.GetFieldValue<byte[]>(5)), volume,
                        volume is not null && chapter is null, rows.GetBoolean(9), ReadText(rows, 10), []);
                    files.Add(id, file);
                    if (locationRoot == root) managed = rows.GetBoolean(8);
                }
                if (ReadText(rows, 12) is { } number)
                {
                    var mappedVolume = rows.GetString(11);
                    file.Chapters.Add(new(mappedVolume.Length == 0 ? null : mappedVolume, number, rows.GetString(13)));
                }
                else if (chapter is not null) file.Chapters.Add(new(volume, chapter, "filename"));
            }
        }
        if (files.Count == 0) return null;
        if (compareFolders) return new(managed, files.Values.ToArray(), []);

        AddWorkKey(command, root);
        command.CommandText = """
            SELECT DISTINCT manifest.volume, chapter.value
            FROM edition_volume_manifest manifest, json_each(manifest.chapters) chapter
            WHERE manifest.work_key = $work OR manifest.work_key = $localWork;
            """;
        using var expectedRows = command.ExecuteReader();
        var expected = new List<SeriesChapter>();
        while (expectedRows.Read()) expected.Add(new(expectedRows.GetString(0), expectedRows.GetString(1)));
        return new(managed, files.Values.ToArray(), expected);
    }

    private const string DuplicateColumns = """
        f.root_id, f.id, folder.path, f.relative_path, f.series_key, f.series_title, f.content_id,
        f.extension, f.size, f.modified_at, c.sha256, f.volume_hint, f.chapter_hint, folder.managed, r.state,
        EXISTS(SELECT 1 FROM artwork.archive_cover art WHERE art.archive_sha = c.sha256),
        (SELECT cover.series_key FROM library_series_cover cover WHERE cover.root_id = f.root_id AND cover.series_key = f.series_key)
        """;

    private const string DuplicateSources = """
        FROM library_file_numbered f JOIN library_content c ON c.id = f.content_id
        JOIN library_folder folder ON folder.root_id = f.root_id JOIN library_root r ON r.id = f.root_id
        """;

    public DuplicateFile? ReadDuplicateFile(Guid root, string fileId)
    {
        using var database = OpenCoverDatabase();
        using var transaction = database.BeginTransaction(deferred: true);
        using var command = database.CreateCommand();
        command.Transaction = transaction;
        command.Parameters.AddWithValue("$root", root);
        command.Parameters.AddWithValue("$file", fileId);
        command.CommandText = "SELECT " + DuplicateColumns + " " + DuplicateSources + " WHERE f.root_id = $root AND f.id = $file;";
        DuplicateFile file;
        using (var rows = command.ExecuteReader())
        {
            if (!rows.Read()) return null;
            file = ReadDuplicateRow(rows);
        }
        return WithDuplicateCoverage(command, file);
    }

    public IReadOnlyList<DuplicateFile> ReadDuplicateCandidates(DuplicateFile source)
    {
        using var database = OpenCoverDatabase();
        using var transaction = database.BeginTransaction(deferred: true);
        using var command = GroupCommand(database, transaction, source.RootId, source.SeriesKey);
        command.Parameters.AddWithValue("$file", source.FileId);
        command.Parameters.AddWithValue("$sameContent", source.ContentId);
        command.Parameters.AddWithValue("$labels", JsonSerializer.Serialize(source.Coverage.Select(item => item.Chapter).Distinct()));
        command.Parameters.AddWithValue("$volume", (object?)(source.ChapterHint is null ? source.VolumeHint : null) ?? DBNull.Value);
        command.CommandText = "WITH covered AS (" + GroupCoverage + ") SELECT " + DuplicateColumns + " " + DuplicateSources + """
             WHERE f.id <> $file AND (f.content_id = $sameContent
                OR (f.root_id = $root AND f.series_key = $series AND (
                    (f.volume_hint = $volume AND f.chapter_hint IS NULL) OR EXISTS(
                    SELECT 1 FROM covered g WHERE g.content_id = f.content_id AND g.chapter IN (SELECT value FROM json_each($labels))))))
             ORDER BY f.content_id = $sameContent DESC, folder.path, f.relative_path;
            """;
        var matches = new List<DuplicateFile>();
        using (var rows = command.ExecuteReader())
        {
            while (rows.Read()) matches.Add(ReadDuplicateRow(rows));
        }
        return matches.Select(file => WithDuplicateCoverage(command, file)).ToArray();
    }

    private static DuplicateFile ReadDuplicateRow(SqliteDataReader rows) => new(rows.GetGuid(0), rows.GetString(1), rows.GetString(2),
        rows.GetString(3), rows.GetString(4), ReadText(rows, 5), rows.GetString(6), rows.GetString(7), rows.GetInt64(8),
        rows.GetDateTimeOffset(9), rows.GetFieldValue<byte[]>(10), ReadText(rows, 11), ReadText(rows, 12), rows.GetBoolean(13),
        rows.GetString(14), rows.GetBoolean(15), ReadText(rows, 16), []);

    private static DuplicateFile WithDuplicateCoverage(SqliteCommand command, DuplicateFile file)
    {
        var content = command.Parameters.Contains("$coverageContent")
            ? command.Parameters["$coverageContent"] : command.Parameters.Add("$coverageContent", SqliteType.Text);
        content.Value = file.ContentId;
        command.CommandText = """
            SELECT volume, chapter, 'manual' FROM content_chapter WHERE content_id = $coverageContent
            UNION ALL
            SELECT inferred.volume, chapter.value, inferred.source
            FROM active_inferred_volume_chapters inferred, json_each(inferred.chapters) chapter
            WHERE inferred.content_id = $coverageContent
                AND NOT EXISTS(SELECT 1 FROM content_chapter c WHERE c.content_id = $coverageContent);
            """;
        var coverage = new List<SeriesChapter>();
        using (var rows = command.ExecuteReader())
        {
            while (rows.Read())
            {
                var volume = rows.GetString(0);
                coverage.Add(new(volume.Length == 0 ? null : volume, rows.GetString(1), rows.GetString(2)));
            }
        }
        if (coverage.Count == 0 && file.ChapterHint is { } label) coverage.Add(new(file.VolumeHint, label, "filename"));
        return file with { Coverage = coverage.OrderBy(item => Numeric(item.Volume)).ThenBy(item => Numeric(item.Chapter)).ToArray() };
    }
}
