using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace MangaOrganizer.Server;

internal sealed record DuplicateFile(Guid RootId, string FileId, string RootPath, string RelativePath, string SeriesKey,
    string? SeriesTitle, string ContentId, string Format, long Size, DateTimeOffset ModifiedAt, byte[] Sha256, string? VolumeHint,
    string? ChapterHint, bool Managed, string State, bool HasCover, string? SeriesCoverKey, IReadOnlyList<SeriesChapter> Coverage);

internal sealed partial class LibraryStore
{
    private const string DuplicateColumns = """
        f.root_id, f.id, folder.path, f.relative_path, f.series_key, f.series_title, f.content_id,
        f.extension, f.size, f.modified_at, c.sha256, f.volume_hint, f.chapter_hint, folder.managed, r.state,
        EXISTS(SELECT 1 FROM artwork.archive_cover art WHERE art.archive_sha = c.sha256),
        (SELECT cover.series_key FROM library_series_cover cover WHERE cover.root_id = f.root_id AND cover.series_key = f.series_key)
        """;

    private const string DuplicateSources = """
        FROM library_file f JOIN library_content c ON c.id = f.content_id
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
        command.CommandText = "WITH covered AS (" + GroupCoverage + ") SELECT " + DuplicateColumns + " " + DuplicateSources + """
             WHERE f.id <> $file AND (f.content_id = $sameContent
                OR (f.root_id = $root AND f.series_key = $series AND EXISTS(
                    SELECT 1 FROM covered g WHERE g.content_id = f.content_id AND g.chapter IN (SELECT value FROM json_each($labels)))))
             ORDER BY f.content_id = $sameContent DESC, folder.path, f.relative_path LIMIT 50;
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
            FROM inferred_volume_chapters inferred, json_each(inferred.chapters) chapter
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
