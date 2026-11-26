using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace MangaOrganizer.Server;

internal static partial class SeriesSuggestions
{
    private const RegexOptions MatchOptions = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    private const string MarkedNumber = @"(?:volume|vol|v|tome|chapter|chap|ch|c)[\s._-]*\d+(?:\.\d+)?(?:\s*[-–]\s*\d+(?:\.\d+)?)?";
    private static readonly HashSet<string> GenericFolders = new(StringComparer.Ordinal)
    {
        "archive", "books", "chapters", "comics", "downloads", "import", "imports", "incoming",
        "intake", "library", "manga", "mangas", "misc", "new", "series", "unsorted", "volumes",
    };

    public static string? CandidateTitle(string relativePath, string rootPath)
    {
        var parts = relativePath.Split('/');
        var fileName = Path.GetFileNameWithoutExtension(parts[^1]);
        var title = ParseTitle(fileName);
        var folder = FindSeriesFolder(parts, rootPath);
        if (title.Length == 0 || NumberOnly().IsMatch(title))
        {
            return folder;
        }

        var normalized = Normalize(title);
        if (folder is not null)
        {
            var normalizedFolder = Normalize(folder);
            if (normalized == normalizedFolder || Normalize(BareNumberSuffix().Replace(title, "")) == normalizedFolder ||
                Normalize(BracketPrefix().Replace(title, "")) == normalizedFolder)
            {
                return folder;
            }
        }

        return normalized.Length > 0 ? title : null;
    }

    public static string Key(string relativePath, string? candidateTitle)
    {
        var input = string.IsNullOrEmpty(candidateTitle) ? "file\0" + relativePath : "series\0" + Normalize(candidateTitle);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
    }

    private static string? FindSeriesFolder(string[] parts, string rootPath)
    {
        for (var index = parts.Length - 2; index >= 0; index--)
        {
            if (SeriesFolderTitle(parts[index]) is { } title)
            {
                return title;
            }
        }

        return SeriesFolderTitle(Path.GetFileName(rootPath));
    }

    private static string? SeriesFolderTitle(string folder)
    {
        var title = ParseTitle(folder);
        var normalized = Normalize(title);
        return normalized.Length == 0 || GenericFolders.Contains(normalized) || NumberOnly().IsMatch(title) ? null : title;
    }

    private static string TrimReleaseTags(string title) => TrailingTags().Replace(title.Trim(), "").Trim();

    private static string ParseTitle(string name)
    {
        var title = ReleasePrefix().Replace(name.Trim(), "");
        if (NumberedSubtitle().IsMatch(title)) return "";

        var annotations = ReleaseAnnotations().Match(title);
        if (annotations.Success)
        {
            var tags = annotations.Groups["tag"].Captures;
            var hasReleaseTag = tags.Any(tag => KnownReleaseTag().IsMatch(tag.Value));
            var hasYear = tags.Any(tag => ReleaseYear().IsMatch(tag.Value));
            if (hasReleaseTag || (hasYear && tags.Count == 1))
            {
                var stem = TrimReleaseTags(title[..annotations.Index]);
                var explicitTitle = RemoveNumberSuffix(stem);
                if (explicitTitle.Length != stem.Length)
                {
                    return explicitTitle;
                }

                // A title can end in a number. Require both a year and a release tag before treating a bare number as a chapter.
                if (hasYear && hasReleaseTag && BareNumberSuffix().Match(stem) is { Success: true } number)
                {
                    return stem[..number.Index];
                }
            }
        }

        return RemoveNumberSuffix(TrimReleaseTags(title));
    }

    private static string RemoveNumberSuffix(string title)
    {
        while (ChapterOrVolumeSuffix().Match(title) is { Success: true } suffix)
        {
            title = title[..suffix.Index].TrimEnd();
        }

        return title;
    }

    private static string Normalize(string title) => WordSeparator().Replace(title.Normalize(NormalizationForm.FormKC), " ")
        .Trim().ToLowerInvariant();

    [GeneratedRegex(@"(?:^|[\s._-]+)" + MarkedNumber + "$", MatchOptions)]
    private static partial Regex ChapterOrVolumeSuffix();

    [GeneratedRegex("^" + MarkedNumber + @"(?:[\s._-]+" + MarkedNumber + @")*\s+[-–—]\s+\S", MatchOptions)]
    private static partial Regex NumberedSubtitle();

    [GeneratedRegex(@"(?:\s*(?:\((?<tag>[^()\[\]]+)\)|\[(?<tag>[^()\[\]]+)\]))+$", MatchOptions)]
    private static partial Regex ReleaseAnnotations();

    [GeneratedRegex(@"^\s*(?:digital|cb[rz7]|zip|rar|7z|(?:group|scans?|scanlations?|fansubs?)\b.*|\d{3,5}\s*[x×]\s*\d{3,5})\s*$", MatchOptions)]
    private static partial Regex KnownReleaseTag();

    [GeneratedRegex(@"^\s*(?:18|19|20|21)[0-9]{2}\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex ReleaseYear();

    [GeneratedRegex(@"^[\s._-]*#?\d+(?:\.\d+)?(?:\s*[-–]\s*\d+(?:\.\d+)?)?[\s._-]*$", MatchOptions)]
    private static partial Regex NumberOnly();

    [GeneratedRegex(@"[\s._-]+\d+(?:\.\d+)?$", MatchOptions)]
    private static partial Regex BareNumberSuffix();

    [GeneratedRegex(@"(?:\s*(?:[\[(](?:ongoing|completed|hiatus)[\])]|\[Variant [0-9]+\]|\[Group\b[^\]]*\]|\[[^\]]*\b(?:scans?|scanlations?|fansubs?)\b[^\]]*\])|[\s._-]+ongoing)+$", MatchOptions)]
    private static partial Regex TrailingTags();

    [GeneratedRegex(@"^\[(?:Group\b[^\]]*|[^\]]*\b(?:scans?|scanlations?|fansubs?)\b[^\]]*)\]\s*", MatchOptions)]
    private static partial Regex ReleasePrefix();

    [GeneratedRegex(@"^\[[^\]]+\]\s*", MatchOptions)]
    private static partial Regex BracketPrefix();

    [GeneratedRegex(@"[^\p{L}\p{M}\p{N}]+", RegexOptions.CultureInvariant)]
    private static partial Regex WordSeparator();
}
