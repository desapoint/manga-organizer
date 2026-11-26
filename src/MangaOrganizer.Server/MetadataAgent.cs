using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json;

namespace MangaOrganizer.Server;

internal sealed record MetadataMatch(int ProviderId, string Title, string? AlternateTitle, string Kind,
    string? Summary, string? Status, int? TotalChapters, int? TotalVolumes);

internal sealed class MetadataLookupException(string message) : Exception(message);

internal sealed class MetadataAgent : IDisposable
{
    private const string TitleFields = """
        id title { english romaji native } description(asHtml:false) status chapters volumes tags { name }
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
        var query = "query($title:String!){Page(page:1,perPage:8){media(search:$title,type:MANGA){" + TitleFields + "}}}";
        var data = await Request(query, new { title }, cancellationToken);
        return (data?.Page?.Media ?? []).OfType<ProviderMedia>().Select(ToMatch).OfType<MetadataMatch>().ToArray();
    }

    public async Task<MetadataMatch?> Read(int id, CancellationToken cancellationToken)
    {
        var query = "query($id:Int!){Media(id:$id,type:MANGA){" + TitleFields + "}}";
        var data = await Request(query, new { id }, cancellationToken);
        return data?.Media is { } media ? ToMatch(media) : null;
    }

    public void Dispose() => client.Dispose();

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

        var doujin = media.Tags?.Any(tag => tag.Name?.Equals("Doujinshi", StringComparison.OrdinalIgnoreCase) == true ||
            tag.Name?.Equals("Doujin", StringComparison.OrdinalIgnoreCase) == true) == true;
        return new(media.Id, titles[0], titles.Skip(1).FirstOrDefault(), doujin ? "doujin" : "manga",
            Truncate(media.Description, 6000), Truncate(media.Status, 40), ValidTotal(media.Chapters), ValidTotal(media.Volumes));
    }

    private static string? Truncate(string? value, int maximum)
    {
        var text = value?.Trim();
        return string.IsNullOrEmpty(text) ? null : text[..Math.Min(text.Length, maximum)];
    }

    private static int? ValidTotal(int? value) => value is > 0 and <= 100_000 ? value : null;

    private sealed record MetadataResponse(MetadataData? Data);
    private sealed record MetadataData(ProviderPage? Page, ProviderMedia? Media);
    private sealed record ProviderPage(ProviderMedia?[]? Media);
    private sealed record ProviderMedia(int Id, ProviderTitle? Title, string? Description, string? Status, int? Chapters,
        int? Volumes, ProviderTag[]? Tags);
    private sealed record ProviderTitle(string? English, string? Romaji, string? Native);
    private sealed record ProviderTag(string? Name);
}
