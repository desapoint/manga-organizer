using System.Text.RegularExpressions;

namespace MangaOrganizer.Server;

internal sealed record DoujinFilename(string Title, string? AlternateTitle, string? Event, string? Creator, string[] Notes)
{
    public static DoujinFilename? Parse(string stem)
    {
        var remaining = stem.Trim();
        var eventPrefix = DoujinPatterns.Event().Match(remaining);
        if (eventPrefix.Success) remaining = remaining[eventPrefix.Length..];
        var creator = DoujinPatterns.Creator().Match(remaining);
        var hasReleaseNotes = DoujinPatterns.KnownNotes().IsMatch(remaining);
        if (!eventPrefix.Success && (!creator.Success || !creator.Groups["creator"].Value.Contains('(') || !hasReleaseNotes)) return null;
        if (creator.Success) remaining = remaining[creator.Length..];

        var notes = new List<string>();
        var suffix = DoujinPatterns.Annotation().Match(remaining);
        while (suffix.Success && suffix.Index > 0)
        {
            notes.Add(suffix.Groups["note"].Value.Trim());
            remaining = DoujinPatterns.TrimTitle(remaining[..suffix.Index]);
            suffix = DoujinPatterns.Annotation().Match(remaining);
        }

        var titles = SplitTitles(remaining.Trim());

        var primary = DoujinPatterns.TrimTitle(titles[0]);
        var alternate = titles.Length == 2 ? DoujinPatterns.TrimTitle(titles[1]) : null;
        notes.Reverse();
        if (eventPrefix.Success && DoujinPatterns.ReleaseRange().Match(primary) is { Success: true } range)
        {
            notes.Insert(0, range.Groups["range"].Value);
            primary = primary[..range.Index];
            if (alternate is not null && SharedRange(titles[0], alternate))
                alternate = DoujinPatterns.ReleaseRange().Replace(alternate, "");
        }

        if (!primary.Any(char.IsLetterOrDigit)) return null;
        return new(primary, string.IsNullOrEmpty(alternate) ? null : alternate,
            eventPrefix.Success ? eventPrefix.Groups["event"].Value.Trim() : null,
            creator.Success ? creator.Groups["creator"].Value.Trim() : null, notes.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    public static string? LookupOverride(string? title)
    {
        if (title is null) return "";
        var lookup = Parse(title)?.Title ?? DoujinPatterns.TrimTitle(title);
        return lookup == title ? null : lookup;
    }

    private static string[] SplitTitles(string title)
    {
        var marked = DoujinPatterns.Alternate().Split(title, 2);
        if (marked.Length == 2) return marked;

        var hyphenated = DoujinPatterns.Hyphen().Split(title, 2);
        if (hyphenated.Length == 2 && SharedRange(hyphenated[0], hyphenated[1])) return hyphenated;

        var bilingual = DoujinPatterns.Bilingual().Match(title);
        if (!bilingual.Success) return marked;
        var primary = bilingual.Groups["primary"].Value;
        var alternate = bilingual.Groups["alternate"].Value;
        var repeated = DoujinPatterns.TitleWord().Matches(primary).Select(word => word.Value)
            .Intersect(DoujinPatterns.TitleWord().Matches(alternate).Select(word => word.Value), StringComparer.OrdinalIgnoreCase);
        return repeated.Any() ? [primary, alternate] : marked;
    }

    private static bool SharedRange(string first, string second)
    {
        var range = DoujinPatterns.ReleaseRange().Match(first).Groups["range"].Value;
        return range.Length != 0 && range == DoujinPatterns.ReleaseRange().Match(second).Groups["range"].Value;
    }
}

internal static partial class DoujinPatterns
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    public static string TrimTitle(string title) => DanglingSeparator().Replace(title.Trim(), "");

    [GeneratedRegex(@"^\((?<event>C\d{2,4}|[^()]*\b(?:Comiket|Comitia|Doujinsai|COMIC1|Reitaisai|Sunshine Creation|Anime Expo)(?:[\s-]*\d{1,4})?\b[^()]*)\)\s*", Options)]
    internal static partial Regex Event();

    [GeneratedRegex(@"^\[(?<creator>[^\[\]]+)\]\s*", Options)]
    internal static partial Regex Creator();

    [GeneratedRegex(@"[\[(](?:English|Japanese|French|Spanish|German|Korean|Chinese|Portuguese|RAW|DL Version|Colorized)[\])]", Options)]
    internal static partial Regex KnownNotes();

    [GeneratedRegex(@"\s*(?:\[(?<note>[^\[\]]+)\]|\{(?<note>[^{}]+)\}|=(?<note>[^=]+)=|\((?<note>[^()]+)\))$", Options)]
    internal static partial Regex Annotation();

    [GeneratedRegex(@"\s+(?:—|\|)\s+", Options)]
    internal static partial Regex Alternate();

    [GeneratedRegex(@"\s+-\s+", Options)]
    internal static partial Regex Hyphen();

    [GeneratedRegex(@"\s+(?<range>\d{1,3}(?:\.\d+)?\s*[-–]\s*\d{1,3}(?:\.\d+)?)$", Options)]
    internal static partial Regex ReleaseRange();

    // A capitalized English lead and a repeated title word help distinguish appended translations from subtitles.
    [GeneratedRegex(@"^(?<primary>.+?\b(?:no|na|wo)\s+.+?)\s+(?<alternate>(?:The|My|Our|Your)\s+.+)$", RegexOptions.CultureInvariant)]
    internal static partial Regex Bilingual();

    [GeneratedRegex(@"\b\p{L}{4,}\b", RegexOptions.CultureInvariant)]
    internal static partial Regex TitleWord();

    [GeneratedRegex(@"(?:\s+[&|–—-])+$", Options)]
    private static partial Regex DanglingSeparator();
}
