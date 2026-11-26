using System.Globalization;
using System.Text.RegularExpressions;

namespace MangaOrganizer.Server;

internal readonly partial record struct BookNumbers(string? Volume, string? Chapter)
{
    internal const string VolumeMarker = "volume|vol|v|tome";
    internal const string ChapterMarker = "chapter|chapitre|chap|ch|c|capítulo|capitulo|cap|kapitel|episode|ep";

    public static BookNumbers FromPath(string path, bool useRangeStart = false)
    {
        var parts = path.Split('/');
        var stem = Path.GetFileNameWithoutExtension(parts[^1]);
        var volume = Find(Volumes(), stem, useRangeStart);
        var chapter = Find(Chapters(), stem, useRangeStart);
        if (volume is null && chapter is null && IsNumber(stem))
        {
            chapter = Canonical(stem);
        }
        for (var index = parts.Length - 2; volume is null && index >= 0; index--)
        {
            volume = Find(Volumes(), parts[index], useRangeStart);
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

    private static string? Find(Regex pattern, string value, bool useRangeStart)
    {
        var match = pattern.Match(value);
        if (!match.Success) return null;
        var remainder = value.AsSpan(match.Index + match.Length).TrimStart();
        if (!useRangeStart && !remainder.IsEmpty && remainder[0] is '-' or '–')
        {
            var end = remainder[1..].TrimStart();
            if (!end.IsEmpty && char.IsAsciiDigit(end[0])) return null;
        }
        return Canonical(match.Groups[1].Value);
    }

    [GeneratedRegex(@"(?:^|[\s._–—-])(?:" + VolumeMarker + @")[\s._-]*([0-9]{1,6}(?:\.[0-9]{1,6})?)(?![0-9]|\.[0-9])(?=$|[\s.\[()_–—-])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Volumes();

    [GeneratedRegex(@"(?:^|[\s._–—-])(?:" + ChapterMarker + @")[\s._-]*([0-9]{1,6}(?:\.[0-9]{1,6})?)(?![0-9]|\.[0-9])(?=$|[\s.\[()_–—-])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Chapters();

    [GeneratedRegex(@"^[0-9]{1,6}(?:\.[0-9]{1,6})?$", RegexOptions.CultureInvariant)]
    private static partial Regex PlainNumber();
}

internal readonly record struct CoverOrder(decimal Number, decimal Chapter) : IComparable<CoverOrder>
{
    public static CoverOrder ForFile(string path, string? seriesTitle)
    {
        var numbers = BookNumbers.FromPath(path, useRangeStart: true);
        var volume = Parse(numbers.Volume);
        var chapter = Parse(numbers.Chapter ?? SeriesSuggestions.BareChapter(path, seriesTitle));
        return new(volume ?? chapter ?? decimal.MaxValue, volume.HasValue ? chapter ?? -1 : -1);
    }

    public int CompareTo(CoverOrder other)
    {
        var number = Number.CompareTo(other.Number);
        return number != 0 ? number : Chapter.CompareTo(other.Chapter);
    }

    private static decimal? Parse(string? number) =>
        decimal.TryParse(number, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) ? value : null;
}
