using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MangaOrganizer.Server;

internal sealed record MetadataMatch(int ProviderId, string Title, string? AlternateTitle, string Kind,
    string? Summary, string? Status, int? TotalChapters, int? TotalVolumes, AniListDetails ProviderDetails)
{
    public string ProviderUrl => MetadataAgent.ListingUrl(ProviderId);
}

internal sealed record AniListDetails(string? CoverUrl, string? Format, string? CountryOfOrigin, int? StartYear, int? EndYear,
    string[] Genres, AniListTag[] Tags, AniListCredit[] Credits);
internal sealed record AniListTag(string Name, bool Spoiler);
internal sealed record AniListCredit(string Name, string? Role);

internal sealed class MetadataLookupException(string message) : Exception(message);

internal sealed partial class MetadataAgent : IDisposable
{
    private const string MediaFields = """
        id title { english romaji native } description(asHtml:false) status chapters volumes
        coverImage { medium large } format countryOfOrigin startDate { year } endDate { year }
        genres tags { name isMediaSpoiler isGeneralSpoiler }
        staff(page:1,perPage:12) { edges { role node { name { full } } } }
        """;
    private readonly HttpClient client;
    private readonly Uri endpoint;

    public MetadataAgent(string? localEndpoint)
    {
        endpoint = new Uri("https://graphql.anilist.co");
        if (!string.IsNullOrEmpty(localEndpoint))
        {
            if (!Uri.TryCreate(localEndpoint, UriKind.Absolute, out var local) || !local.IsLoopback ||
                local.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(local.UserInfo))
            {
                throw new InvalidOperationException("The metadata service override must be a loopback HTTP(S) endpoint.");
            }
            endpoint = local;
        }

        client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromSeconds(12),
            MaxResponseContentBufferSize = 1024 * 1024,
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MangaOrganizer/0.1");
    }

    public async Task<IReadOnlyList<MetadataMatch>> Search(string title, CancellationToken cancellationToken)
    {
        var query = "query($title:String!){Page(page:1,perPage:8){media(search:$title,type:MANGA){" + MediaFields + "}}}";
        var data = await Request(query, new { title }, cancellationToken);
        return (data?.Page?.Media ?? []).OfType<ProviderMedia>().Select(ToMatch).OfType<MetadataMatch>().ToArray();
    }

    public async Task<MetadataMatch?> Read(int id, CancellationToken cancellationToken)
    {
        var query = "query($id:Int!){Media(id:$id,type:MANGA){" + MediaFields + "}}";
        var data = await Request(query, new { id }, cancellationToken);
        return data?.Media is { } media ? ToMatch(media) : null;
    }

    public void Dispose() => client.Dispose();

    public static string ListingUrl(int id) => $"https://anilist.co/manga/{id}";

    private async Task<MetadataData?> Request(string query, object variables, CancellationToken cancellationToken)
    {
        try
        {
            using var content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(new { query, variables }));
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            using var response = await client.PostAsync(endpoint, content, cancellationToken);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                throw new MetadataLookupException("The metadata provider is rate-limiting requests. Try again shortly.");
            }
            if (!response.IsSuccessStatusCode)
            {
                throw new MetadataLookupException("The metadata provider is unavailable. You can enter details manually.");
            }

            var body = await response.Content.ReadFromJsonAsync<MetadataResponse>(cancellationToken);
            if (body?.Data is null)
            {
                throw new MetadataLookupException("The metadata provider did not return a usable result.");
            }

            return body.Data;
        }
        catch (Exception error) when (error is HttpRequestException or JsonException ||
            error is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new MetadataLookupException("The metadata provider could not be reached. Try again or enter details manually.");
        }
    }

    private static MetadataMatch? ToMatch(ProviderMedia media)
    {
        var titles = new[] { media.Title?.English, media.Title?.Romaji, media.Title?.Native }
            .Select(title => Truncate(title, 300)).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (media.Id <= 0 || titles.Length == 0) return null;

        var allTags = (media.Tags ?? []).OfType<ProviderTag>().ToArray();
        var doujin = allTags.Any(tag => string.Equals(tag.Name, "Doujinshi", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(tag.Name, "Doujin", StringComparison.OrdinalIgnoreCase));
        var tags = allTags.Where(tag => !string.IsNullOrWhiteSpace(tag.Name))
            .Select(tag => new AniListTag(Truncate(tag.Name, 100)!, tag.IsMediaSpoiler == true || tag.IsGeneralSpoiler == true))
            .DistinctBy(tag => tag.Name, StringComparer.OrdinalIgnoreCase).Take(64).ToArray();
        var credits = (media.Staff?.Edges ?? []).OfType<ProviderCredit>().Where(credit => !string.IsNullOrWhiteSpace(credit.Node?.Name?.Full))
            .Select(credit => new AniListCredit(Truncate(credit.Node?.Name?.Full, 150)!, Truncate(credit.Role, 100)))
            .Distinct().Take(12).ToArray();
        var genres = (media.Genres ?? []).Select(name => Truncate(name, 100)).OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToArray();
        var country = media.CountryOfOrigin is { Length: 2 } code && code.All(char.IsAsciiLetter) ? code.ToUpperInvariant() : null;
        var details = new AniListDetails(CoverUrl(media.CoverImage?.Medium) ?? CoverUrl(media.CoverImage?.Large),
            Truncate(media.Format, 40), country, ValidYear(media.StartDate), ValidYear(media.EndDate), genres, tags, credits);
        return new(media.Id, titles[0], titles.Skip(1).FirstOrDefault(), doujin ? "doujin" : "manga",
            Truncate(PlainDescription(media.Description), 6000), Truncate(media.Status, 40), ValidTotal(media.Chapters), ValidTotal(media.Volumes), details);
    }

    internal static string? PlainDescription(string? value)
    {
        if (value is null) return null;
        var text = WebUtility.HtmlDecode(value);
        text = DescriptionBreaks().Replace(text, "\n");
        return DescriptionFormatting().Replace(text, "").Trim();
    }

    [GeneratedRegex(@"<br\s*/?>|</p\s*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DescriptionBreaks();

    [GeneratedRegex(@"</?(?:p|i|b|em|strong|u|s|del|sup|sub)\s*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DescriptionFormatting();

    private static string? CoverUrl(string? value)
    {
        return value is { Length: <= 2048 } && Uri.TryCreate(value, UriKind.Absolute, out var url) &&
            url.Scheme == Uri.UriSchemeHttps && url.IsDefaultPort && url.UserInfo.Length == 0 &&
            url.Host is "s4.anilist.co" or "s3.anilist.co" &&
            url.AbsolutePath.StartsWith("/file/anilistcdn/media/manga/cover/", StringComparison.Ordinal)
            ? url.AbsoluteUri : null;
    }

    private static string? Truncate(string? value, int maximum)
    {
        var text = value?.Trim();
        return string.IsNullOrEmpty(text) ? null : text[..Math.Min(text.Length, maximum)];
    }

    private static int? ValidTotal(int? value) => value is > 0 and <= 100_000 ? value : null;
    private static int? ValidYear(ProviderDate? value) => value?.Year is > 0 and <= 9999 ? value.Year : null;

    private sealed record MetadataResponse(MetadataData? Data);
    private sealed record MetadataData(ProviderPage? Page, ProviderMedia? Media);
    private sealed record ProviderPage(ProviderMedia?[]? Media);
    private sealed record ProviderMedia(int Id, ProviderTitle? Title, string? Description, string? Status, int? Chapters,
        int? Volumes, ProviderTag?[]? Tags, string?[]? Genres, string? Format, string? CountryOfOrigin, ProviderDate? StartDate,
        ProviderDate? EndDate, ProviderCover? CoverImage, ProviderStaff? Staff);
    private sealed record ProviderTitle(string? English, string? Romaji, string? Native);
    private sealed record ProviderTag(string? Name, bool? IsMediaSpoiler, bool? IsGeneralSpoiler);
    private sealed record ProviderDate(int? Year);
    private sealed record ProviderCover(string? Medium, string? Large);
    private sealed record ProviderStaff(ProviderCredit?[]? Edges);
    private sealed record ProviderCredit(string? Role, ProviderPerson? Node);
    private sealed record ProviderPerson(ProviderName? Name);
    private sealed record ProviderName(string? Full);
}
