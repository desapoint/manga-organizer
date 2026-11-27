namespace MangaOrganizer.Server;

internal sealed record SeriesTag(string Name, bool Spoiler)
{
    public static bool TryNormalize(SeriesTag?[]? input, out SeriesTag[]? tags)
    {
        tags = null;
        if (input is null) return true;
        if (input.Length > 100) return false;

        var normalized = new Dictionary<string, SeriesTag>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in input)
        {
            if (item?.Name?.Trim() is not { Length: > 0 and <= 100 } name || name.Any(char.IsControl))
                return false;

            if (normalized.TryGetValue(name, out var prior))
                normalized[name] = prior with { Spoiler = prior.Spoiler || item.Spoiler };
            else
                normalized.Add(name, new(name, item.Spoiler));
        }

        tags = normalized.Values.ToArray();
        return true;
    }

    public const string InvalidMessage = "Enter up to 100 tags with 1–100 characters per tag.";
}
