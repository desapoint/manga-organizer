using System.Text.RegularExpressions;

namespace MangaOrganizer.Server;

internal readonly partial record struct BookNumbers(string? Volume, string? Chapter)
{
    public static BookNumbers FromPath(string path)
    {
        var parts = path.Split('/');
        var stem = Path.GetFileNameWithoutExtension(parts[^1]);
        var volume = Find(Volumes(), stem);
        var chapter = Find(Chapters(), stem);
        if (volume is null && chapter is null && IsNumber(stem))
        {
            chapter = Canonical(stem);
        }
        for (var index = parts.Length - 2; volume is null && index >= 0; index--)
        {
            volume = Find(Volumes(), parts[index]);
        }
        return new(volume, chapter);
    }

    public static bool IsNumber(string value) => PlainNumber().IsMatch(value);

    public static string Canonical(string value)
    {
        var parts = value.Split('.', 2);
        var whole = parts[0].TrimStart('0');
        if (whole.Length == 0) whole = "0";
        var fraction = parts.Length == 2 ? parts[1].TrimEnd('0') : "";
        return fraction.Length == 0 ? whole : whole + "." + fraction;
    }

    private static string? Find(Regex pattern, string value)
    {
        var match = pattern.Match(value);
        if (!match.Success) return null;
        var remainder = value.AsSpan(match.Index + match.Length).TrimStart();
        if (!remainder.IsEmpty && remainder[0] is '-' or '–')
        {
            var end = remainder[1..].TrimStart();
            if (!end.IsEmpty && char.IsAsciiDigit(end[0])) return null;
        }
        return Canonical(match.Groups[1].Value);
    }

    [GeneratedRegex(@"(?:^|[\s._-])(?:v|vol|volume)[\s._-]*([0-9]{1,6}(?:\.[0-9]{1,6})?)(?![0-9]|\.[0-9])(?=$|[\s.\[()_–-])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Volumes();

    [GeneratedRegex(@"(?:^|[\s._-])(?:c|ch|chap|chapter)[\s._-]*([0-9]{1,6}(?:\.[0-9]{1,6})?)(?![0-9]|\.[0-9])(?=$|[\s.\[()_–-])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Chapters();

    [GeneratedRegex(@"^[0-9]{1,6}(?:\.[0-9]{1,6})?$", RegexOptions.CultureInvariant)]
    private static partial Regex PlainNumber();
}
