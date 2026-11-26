using System.Globalization;
using System.Text.RegularExpressions;

namespace MangaOrganizer.Server;

internal sealed record DetectedVolumeChapters(string[] Chapters, string Source);

internal static partial class VolumeChapterDetection
{
    private const int MaxChapters = 500;
    private const RegexOptions Parsing = RegexOptions.CultureInvariant | RegexOptions.IgnoreCase;

    public static DetectedVolumeChapters? Read(Stream file, string relativePath, CancellationToken cancellationToken)
    {
        using var archive = ArchivePages.Open(file);
        if (archive is null) return null;
        var pages = ArchivePages.Entries(archive).ToArray();
        if (pages.Length == 0) return null;

        var filename = FromFilename(Path.GetFileNameWithoutExtension(relativePath));
        var folders = FromPageFolders(pages, cancellationToken);
        if (filename is not null && folders is not null && !filename.SequenceEqual(folders)) return null;

        return folders is not null ? new(folders, "archive") : filename is not null ? new(filename, "filename") : null;
    }

    private static string[]? FromFilename(string name)
    {
        var matches = ExplicitRange().Matches(name);
        if (matches.Count != 1) return null;
        var range = matches[0];
        var start = int.Parse(range.Groups[1].Value, CultureInfo.InvariantCulture);
        var end = int.Parse(range.Groups[2].Value, CultureInfo.InvariantCulture);
        var count = end - start + 1;
        return count is >= 2 and <= MaxChapters
            ? Enumerable.Range(start, count).Select(number => number.ToString(CultureInfo.InvariantCulture)).ToArray() : null;
    }

    private static string[]? FromPageFolders(IReadOnlyList<ComicEntry> pages, CancellationToken cancellationToken)
    {
        var chapters = new HashSet<string>(StringComparer.Ordinal);
        foreach (var page in pages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (CoverArt.IsNamedCover(page)) continue;
            var parents = page.FullName.Split('/');
            var chapter = "";
            foreach (var folder in parents.SkipLast(1))
            {
                var match = ChapterFolder().Match(folder);
                if (!match.Success) continue;
                var label = BookNumbers.Canonical(match.Groups[1].Value);
                if (chapter.Length != 0 && chapter != label) return null;
                chapter = label;
            }
            if (chapter.Length == 0) return null;
            chapters.Add(chapter);
            if (chapters.Count > MaxChapters) return null;
        }

        return chapters.Count > 0 ? chapters.OrderBy(label => decimal.Parse(label, CultureInfo.InvariantCulture)).ToArray() : null;
    }

    [GeneratedRegex(@"(?:^|[\s._–—-])(?:" + BookNumbers.ChapterMarker +
        @")[\s._-]*([0-9]{1,6})\s*[-–—]\s*([0-9]{1,6})(?![0-9]|\.[0-9]|\s*[-–—]\s*[0-9])(?=$|[\s.\[()_–—-])", Parsing)]
    private static partial Regex ExplicitRange();

    [GeneratedRegex(@"^(?:" + BookNumbers.ChapterMarker + @")[\s._-]*([0-9]{1,6}(?:\.[0-9]{1,6})?)(?:\s+[-–—]\s+\S.*)?$", Parsing)]
    private static partial Regex ChapterFolder();
}
