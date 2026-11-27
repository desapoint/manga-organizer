using System.Text;

namespace MangaOrganizer.Server;

internal static class IntakeNaming
{
    public static string PathFor(string original, string title, int? year, string? volume, string? chapter,
        IReadOnlyList<string[]> assigned, bool oneShot)
    {
        title = SafeTitle(title);
        if (title.Length == 0) return original;
        var folder = year is > 0 ? $"{title} ({year})" : title;
        if (assigned.Count > 0)
        {
            var volumeLabels = assigned.Select(item => item[0]).Distinct().ToArray();
            volume = volumeLabels is [var label] && label.Length > 0 ? label : volume;
            if (assigned.Count == 1) chapter = assigned[0][1];
            else if (volume is null || chapter is not null)
                chapter = string.Join("+", assigned.Select(item => item[1]).Distinct());
        }
        var suffix = volume is null ? "" : $" Vol.{volume}";
        if (!oneShot && chapter is not null)
            suffix += $" Ch.{chapter}";
        var filename = title + suffix + Path.GetExtension(original);
        return Encoding.UTF8.GetByteCount(filename) <= 220 ? $"{folder}/{filename}" : original;
    }

    private static string SafeTitle(string input)
    {
        const string disallowed = "<>:\"/\\|?*";
        var result = new StringBuilder();
        foreach (var character in input.EnumerateRunes())
        {
            if (result.Length + character.Utf16SequenceLength > 75) break;
            if (character.IsAscii && (Rune.IsControl(character) || disallowed.Contains((char)character.Value)))
                result.Append(' ');
            else
                result.Append(character.ToString());
        }
        var safe = string.Join(' ', result.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).TrimEnd('.', ' ');
        // Device names form reserved path components on Windows, even when followed by a dot.
        var stem = safe.Split('.', 2)[0].ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" ||
            stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal))
                && "0123456789¹²³".Contains(stem[3]))
            safe = "_" + safe;
        return safe.StartsWith(".manga-organizer-", StringComparison.OrdinalIgnoreCase) ? "_" + safe : safe;
    }
}
