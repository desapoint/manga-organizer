using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;

namespace MangaOrganizer.Server;

internal sealed record SeriesCoverChange(string? CandidateId, bool UseProvider);

internal static class SeriesCoverEndpoints
{
    public static void MapSeriesCovers(this RouteGroupBuilder library)
    {
        var series = library.MapGroup("/{root:guid}/series/{seriesKey}");
        series.MapGet("/cover-options", (Guid root, string seriesKey, bool? published, int? offset, int? limit, LibraryStore store) =>
        {
            if (!LibraryEndpoints.IsSeriesKey(seriesKey) || !LibraryEndpoints.IsValidPage(offset, limit)) return Results.BadRequest();
            return store.ReadCoverChoices(root, seriesKey.ToLowerInvariant(), published == true, offset ?? 0, limit ?? 20) is { } options
                ? Results.Ok(options) : Results.NotFound();
        });

        series.MapGet("/cover-options/{candidate}", (Guid root, string seriesKey, string candidate, bool? published, LibraryStore store,
            HttpContext context) =>
            LibraryEndpoints.IsSeriesKey(seriesKey) && LibraryEndpoints.IsSeriesKey(candidate)
                ? LibraryEndpoints.CoverResult(context, store.ReadCoverCandidate(root, seriesKey.ToLowerInvariant(), candidate, published == true))
                : Results.NotFound());

        series.MapPut("/cover-preference", async (Guid root, string seriesKey, bool? published, SeriesCoverChange change,
            LibraryStore store, MetadataAgent agent, HttpContext context, IAntiforgery antiforgery, CancellationToken cancellationToken) =>
        {
            if (!await antiforgery.IsRequestValidAsync(context) || !LibraryEndpoints.IsSeriesKey(seriesKey) ||
                (change.CandidateId is null ? !change.UseProvider : change.UseProvider || !LibraryEndpoints.IsSeriesKey(change.CandidateId)))
                return Results.BadRequest();

            var key = seriesKey.ToLowerInvariant();
            if (change.UseProvider)
            {
                if (store.ReadProviderCoverSource(root, key, published == true) is not { } source) return Results.NotFound();
                var cover = await agent.DownloadCover(source.Large, source.Thumbnail, cancellationToken);
                if (cover is null)
                    return Results.Json(new { message = "Could not save a local copy of the AniList cover. Try again." }, statusCode: 502);

                return store.SetSeriesCover(root, key, published == true, upload: cover, expectedProvider: source)
                    ? Results.NoContent() : Results.Conflict(new { message = "The AniList cover changed while downloading. Reopen the cover options." });
            }
            return store.SetSeriesCover(root, key, published == true, change.CandidateId)
                ? Results.NoContent() : Results.NotFound();
        }).WithMetadata(new RequestSizeLimitAttribute(4096));

        series.MapPut("/cover-upload", async (Guid root, string seriesKey, bool? published, LibraryStore store, HttpContext context,
            IAntiforgery antiforgery, CancellationToken cancellationToken) =>
        {
            if (!await antiforgery.IsRequestValidAsync(context) || !LibraryEndpoints.IsSeriesKey(seriesKey)) return Results.BadRequest();
            if (context.Request.ContentLength is not { } length || length is < 24 or > RasterImage.MaxCoverBytes)
                return Results.BadRequest(new { message = "Choose a JPEG, PNG or WebP image up to 8 MiB." });

            var buffer = new byte[(int)length];
            try
            {
                await context.Request.Body.ReadExactlyAsync(buffer, cancellationToken);
            }
            catch (EndOfStreamException)
            {
                return Results.BadRequest(new { message = "The image upload was incomplete." });
            }
            using var stream = new MemoryStream(buffer);
            if (RasterImage.Read(stream, length, RasterImage.MaxCoverBytes) is not { } image || CoverCache.Encode(image) is not { } encoded)
                return Results.BadRequest(new { message = "Choose a valid JPEG, PNG or WebP image within the supported dimensions." });

            return store.SetSeriesCover(root, seriesKey.ToLowerInvariant(), published == true, upload: encoded)
                ? Results.NoContent() : Results.NotFound();
        }).WithMetadata(new RequestSizeLimitAttribute(RasterImage.MaxCoverBytes));

        series.MapDelete("/cover-preference", async (Guid root, string seriesKey, bool? published, LibraryStore store,
            HttpContext context, IAntiforgery antiforgery) =>
        {
            if (!await antiforgery.IsRequestValidAsync(context) || !LibraryEndpoints.IsSeriesKey(seriesKey)) return Results.BadRequest();
            return store.SetSeriesCover(root, seriesKey.ToLowerInvariant(), published == true) ? Results.NoContent() : Results.NotFound();
        });
    }
}
