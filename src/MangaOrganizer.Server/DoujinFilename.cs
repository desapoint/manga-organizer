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
            remaining = remaining[..suffix.Index].TrimEnd();
            suffix = DoujinPatterns.Annotation().Match(remaining);
        }

        var split = DoujinPatterns.Alternate().Split(remaining.Trim(), 2);
        var title = split[0].Trim();
        if (title.Length == 0 || !title.Any(char.IsLetterOrDigit)) return null;
        notes.Reverse();
        return new(title, split.Length == 2 ? split[1].Trim() : null,
            eventPrefix.Success ? eventPrefix.Groups["event"].Value.Trim() : null,
            creator.Success ? creator.Groups["creator"].Value.Trim() : null, notes.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }
}

internal static partial class DoujinPatterns
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    [GeneratedRegex(@"^\((?<event>C\d{2,4}|[^()]*\b(?:Comiket|Comitia|Doujinsai|COMIC1|Reitaisai|Sunshine Creation|Anime Expo)\b[^()]*)\)\s*", Options)]
    internal static partial Regex Event();

    [GeneratedRegex(@"^\[(?<creator>[^\[\]]+)\]\s*", Options)]
    internal static partial Regex Creator();

    [GeneratedRegex(@"\[(?:English|Japanese|French|Spanish|German|Korean|Chinese|Portuguese|RAW|DL Version|Colorized)\]", Options)]
    internal static partial Regex KnownNotes();

    [GeneratedRegex(@"\s*(?:\[(?<note>[^\[\]]+)\]|\{(?<note>[^{}]+)\}|=(?<note>[^=]+)=|\((?<note>[^()]+)\))$", Options)]
    internal static partial Regex Annotation();

    [GeneratedRegex(@"\s+(?:—|\|)\s+", Options)]
    internal static partial Regex Alternate();
}
