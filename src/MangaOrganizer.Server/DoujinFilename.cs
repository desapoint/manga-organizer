using System.Text.RegularExpressions;

namespace MangaOrganizer.Server;

internal sealed record DoujinFilename(string Title, string? AlternateTitle, string? Event, string? Creator, string[] Notes)
{
    public static DoujinFilename? Parse(string stem)
    {
        var remaining = stem.Trim();
        var order = DoujinPatterns.ReleaseOrder().Match(remaining);
        if (order.Success) remaining = remaining[order.Length..];
        var eventPrefix = DoujinPatterns.Event().Match(remaining);
        if (eventPrefix.Success) remaining = remaining[eventPrefix.Length..];
        else
        {
            var possibleEvent = DoujinPatterns.PairedEvent().Match(remaining);
            if (possibleEvent.Success && DoujinPatterns.KnownNotes().IsMatch(remaining[possibleEvent.Length..]))
            {
                eventPrefix = possibleEvent;
                remaining = remaining[eventPrefix.Length..];
            }
        }

        var creator = DoujinPatterns.Creator().Match(remaining);
        var creatorWithReleaseNotes = creator.Groups["creator"].Value.Contains('(') && DoujinPatterns.KnownNotes().IsMatch(remaining);
        if (!eventPrefix.Success && (!creator.Success || !order.Success && !creatorWithReleaseNotes)) return null;
        if (creator.Success) remaining = remaining[creator.Length..];

        var notes = new List<string>();
        var suffix = DoujinPatterns.Annotation().Match(remaining);
        while (suffix.Success && suffix.Index > 0)
        {
            notes.Add(suffix.Groups["note"].Value.Trim());
            remaining = DoujinPatterns.TrimTitle(remaining[..suffix.Index]);
            suffix = DoujinPatterns.Annotation().Match(remaining);
        }

        if (DoujinPatterns.Extras().Match(remaining) is { Success: true } extras)
        {
            notes.Add(extras.Groups["note"].Value);
            remaining = remaining[..extras.Index];
        }
        notes.Reverse();
        if (order.Success) notes.Insert(0, order.Groups["order"].Value);

        var titles = SplitTitles(remaining.Trim());

        var primary = DoujinPatterns.TrimTitle(titles[0]);
        var alternate = titles.Length == 2 ? DoujinPatterns.TrimTitle(titles[1]) : null;
        if (eventPrefix.Success && DoujinPatterns.ReleaseRange().Match(primary) is { Success: true } range)
        {
            notes.Insert(0, range.Groups["number"].Value);
            primary = primary[..range.Index];
            if (alternate is not null && SharedEnding(DoujinPatterns.ReleaseRange(), titles[0], alternate))
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
        if (hyphenated.Length == 2 && (SharedEnding(DoujinPatterns.ReleaseRange(), hyphenated[0], hyphenated[1]) ||
            SharedEnding(DoujinPatterns.Installment(), hyphenated[0], hyphenated[1]))) return hyphenated;

        if (DoujinPatterns.UnderscoredAlternate().Match(title) is { Success: true } enclosed)
            return [title[..enclosed.Index], enclosed.Groups["alternate"].Value];

        var bilingual = DoujinPatterns.Bilingual().Match(title);
        if (!bilingual.Success) return marked;
        var primary = bilingual.Groups["primary"].Value;
        var alternate = bilingual.Groups["alternate"].Value;
        var repeated = DoujinPatterns.TitleWord().Matches(primary).Select(word => word.Value)
            .Intersect(DoujinPatterns.TitleWord().Matches(alternate).Select(word => word.Value), StringComparer.OrdinalIgnoreCase);
        return repeated.Any() ? [primary, alternate] : marked;
    }

    private static bool SharedEnding(Regex pattern, string first, string second)
    {
        var ending = pattern.Match(first).Groups["number"].Value;
        return ending.Length != 0 && ending == pattern.Match(second).Groups["number"].Value;
    }
}

internal static partial class DoujinPatterns
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    public static string TrimTitle(string title) => DanglingSeparator().Replace(title.Trim(), "");

    [GeneratedRegex(@"^\((?<event>C\d{2,4}|CR[\s-]*\d{1,4}|SC[\s-]*\d{1,4}(?:\s+[^()]+)?|Doujinshi|[^()]*\b(?:Comiket|Comitia|Doujinsai|COMIC1|Reitaisai|Sunshine Creation|Anime Expo|Puniket)(?:[\s-]*\d{1,4})?\b[^()]*)\)\s*", Options)]
    internal static partial Regex Event();

    [GeneratedRegex(@"^\((?<event>[^()]+)\)\s*(?=\[)", Options)]
    internal static partial Regex PairedEvent();

    [GeneratedRegex(@"^(?<order>\d{1,3}[.)])\s*(?=[\[(])", Options)]
    internal static partial Regex ReleaseOrder();

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

    [GeneratedRegex(@"\s+(?<number>\d{1,3}(?:\.\d+)?\s*[-–]\s*\d{1,3}(?:\.\d+)?)$", Options)]
    internal static partial Regex ReleaseRange();

    [GeneratedRegex(@"\s+(?<number>\d{1,3}(?:\.\d+)?)$", Options)]
    internal static partial Regex Installment();

    [GeneratedRegex(@"\s+_(?<alternate>[^_]+)_$", Options)]
    internal static partial Regex UnderscoredAlternate();

    [GeneratedRegex(@"\s+\+\s*(?<note>omake|bonus(?:\s+chapter)?|extras?)$", Options)]
    internal static partial Regex Extras();

    // A capitalized English lead and a repeated title word help distinguish appended translations from subtitles.
    [GeneratedRegex(@"^(?<primary>.+?\b(?:no|na|wo)\s+.+?)\s+(?<alternate>(?:The|My|Our|Your)\s+.+)$", RegexOptions.CultureInvariant)]
    internal static partial Regex Bilingual();

    [GeneratedRegex(@"\b\p{L}{4,}\b", RegexOptions.CultureInvariant)]
    internal static partial Regex TitleWord();

    [GeneratedRegex(@"(?:\s+[&|–—-])+$", Options)]
    private static partial Regex DanglingSeparator();
}
