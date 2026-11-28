using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;

namespace MangaOrganizer.Server;

internal sealed record FileCoverageSuggestion(string? Volume, string[] Chapters);
internal sealed record FileCoverageState(string? Volume, string[] Chapters, bool MultipleVolumes, string Revision,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] FileCoverageSuggestion? Suggestion = null);
internal sealed record FileCoverageEdit(string FileId, string ContentId, string SeriesKey, string Revision,
    string Volume, string[] Chapters);

internal sealed partial class LibraryStore
{
    private const string InferredFileVolume = """
        (SELECT inferred.volume FROM active_inferred_volume_chapters inferred WHERE inferred.content_id = f.content_id)
        """;

    private const string AssignedFileChapters = """
        (SELECT json_group_array(json_array(chapter.volume, chapter.chapter)
            ORDER BY CAST(chapter.chapter AS REAL), chapter.chapter, chapter.volume)
         FROM content_chapter chapter WHERE chapter.content_id = f.content_id)
        """;

    private const string InferredFileChapters = """
        (SELECT inferred.chapters FROM active_inferred_volume_chapters inferred WHERE inferred.content_id = f.content_id)
        """;

    private static FileCoverageState FileCoverage(string? volumeHint, string? chapterHint, bool oneShot,
        string? inferredVolume, string[][] assigned, string? manualVolume, string? inferredChapters)
    {
        var revision = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            volumeHint, chapterHint, oneShot, inferredVolume, assigned, manualVolume, inferredChapters,
        })));
        if (assigned.Length > 0)
        {
            var volumes = assigned.Select(pair => pair[0]).Distinct(StringComparer.Ordinal).ToArray();
            var volume = volumes is [var single] && single.Length > 0 ? single : null;
            return new(volume, assigned.Select(pair => pair[1]).ToArray(), volumes.Length > 1, revision);
        }
        if (manualVolume is not null) return new(manualVolume, [], false, revision);
        if (inferredChapters is not null)
            return new(inferredVolume, JsonSerializer.Deserialize<string[]>(inferredChapters) ?? [], false, revision);
        var chapter = chapterHint ?? (oneShot ? "1" : null);
        return new(volumeHint, chapter is null ? [] : [chapter], false, revision);
    }

    private static FileCoverageSuggestion? SuggestFileCoverage(string path, FileCoverageState current,
        string? savedTitle, string[] alternateTitles, string? scannedTitle)
    {
        var numbers = BookNumbers.FromPath(path);
        var volume = current.Volume ?? numbers.Volume;
        var chapter = current.Chapters.Length == 0
            ? numbers.Chapter ?? SeriesSuggestions.ChapterForKnownTitles(path, savedTitle, alternateTitles, scannedTitle)
            : null;
        return chapter is not null ? new(volume, [chapter]) :
            volume != current.Volume ? new(volume, current.Chapters) : null;
    }

    public bool SaveFileCoverage(Guid root, IReadOnlyList<FileCoverageEdit> edits, bool published)
    {
        using var database = OpenDatabase();
        using var transaction = database.BeginTransaction(deferred: false);
        using var check = database.CreateCommand();
        check.Transaction = transaction;
        check.Parameters.AddWithValue("$root", root);
        var file = check.Parameters.Add("$file", SqliteType.Text);
        var content = check.Parameters.Add("$content", SqliteType.Text);
        var series = check.Parameters.Add("$series", SqliteType.Text);
        check.CommandText = $"""
            SELECT f.volume_hint, f.chapter_hint, {FinishedOneShot}, {InferredFileVolume}, {AssignedFileChapters},
                c.manual_volume, {InferredFileChapters}
            FROM library_file_numbered f JOIN library_content c ON c.id = f.content_id
            WHERE f.root_id = $root AND f.id = $file AND f.content_id = $content AND f.series_key = $series
            AND NOT EXISTS (SELECT 1 FROM file_operation WHERE content_id = $content AND state IN ('applying', 'restoring'))
            {SeriesVisibility(published)};
            """;
        foreach (var edit in edits)
        {
            file.Value = edit.FileId;
            content.Value = edit.ContentId;
            series.Value = edit.SeriesKey;
            using var rows = check.ExecuteReader();
            if (!rows.Read()) return false;
            var current = FileCoverage(ReadText(rows, 0), ReadText(rows, 1), rows.GetBoolean(2), ReadText(rows, 3),
                JsonSerializer.Deserialize<string[][]>(rows.GetString(4)) ?? [], ReadText(rows, 5), ReadText(rows, 6));
            if (current.MultipleVolumes || current.Revision != edit.Revision) return false;
        }

        using var write = database.CreateCommand();
        write.Transaction = transaction;
        foreach (var edit in edits)
        {
            write.Parameters.Clear();
            write.Parameters.AddWithValue("$content", edit.ContentId);
            WriteCoverage(write, edit.Volume, edit.Chapters, edit.Volume.Length > 0 && edit.Chapters.Length == 0);
        }
        transaction.Commit();
        return true;
    }
}
