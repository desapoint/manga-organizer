namespace MangaOrganizer.Server;

internal sealed partial class MetadataAgent
{
    public async Task<byte[]?> DownloadCover(string? large, string thumbnail, CancellationToken cancellationToken)
    {
        foreach (var source in new[] { large, thumbnail }.OfType<string>().Distinct(StringComparer.Ordinal))
        {
            if (CoverUrl(source) is not { } validated) continue;
            var asset = new Uri(validated);
            var url = endpoint.IsLoopback ? new Uri(endpoint, asset.PathAndQuery) : asset;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(10));
            try
            {
                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
                if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength is > RasterImage.MaxCoverBytes) continue;
                using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
                using var data = new MemoryStream();
                var buffer = new byte[64 * 1024];
                while (data.Length <= RasterImage.MaxCoverBytes)
                {
                    var read = await stream.ReadAsync(buffer, deadline.Token);
                    if (read == 0) break;
                    data.Write(buffer, 0, read);
                }
                data.Position = 0;
                if (RasterImage.Read(data, data.Length, RasterImage.MaxCoverBytes) is { } image &&
                    CoverCache.Encode(image) is { } encoded) return encoded;
            }
            catch (Exception error) when (error is HttpRequestException or IOException ||
                error is OperationCanceledException && !cancellationToken.IsCancellationRequested)
            {
                // A failed larger image can still have a usable thumbnail.
            }
        }
        return null;
    }
}
