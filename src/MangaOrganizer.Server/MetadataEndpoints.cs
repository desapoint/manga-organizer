using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;

namespace MangaOrganizer.Server;

internal sealed record MetadataChange(int? ProviderId, string? Title, string? Kind, string? AlternateTitle, string? Summary,
    string? Status, int? TotalChapters, int? TotalVolumes);
internal sealed record CoverageChange(string? ContentId, string? Volume, string[]? Chapters);

internal static class MetadataEndpoints
{
    public static void MapSeriesMetadata(this RouteGroupBuilder library)
    {
        var series = library.MapGroup("/{root:guid}/series/{seriesKey}");
        series.MapGet("/details", (Guid root, string seriesKey, LibraryStore store, HttpContext context) =>
        {
            if (!ReaderEndpoints.TryOwner(context, out var owner)) return Results.Unauthorized();
            return LibraryEndpoints.IsSeriesKey(seriesKey) && store.ReadSeriesDetails(root, seriesKey.ToLowerInvariant(), owner) is { } details
                ? Results.Ok(details) : Results.NotFound();
        });
        series.MapGet("/coverage", (Guid root, string seriesKey, string? contentId, LibraryStore store) =>
            LibraryEndpoints.IsSeriesKey(seriesKey) && IsContentId(contentId) &&
                store.ReadCoverage(root, seriesKey.ToLowerInvariant(), contentId!.ToLowerInvariant()) is { } coverage
                ? Results.Ok(coverage) : Results.NotFound());
        series.MapGet("/metadata/search", async (Guid root, string seriesKey, string? title, LibraryStore store,
            MetadataAgent agent, CancellationToken cancellationToken) =>
        {
            if (!LibraryEndpoints.IsSeriesKey(seriesKey) || !store.TryGetRoot(root, out _)) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(title) || title.Length > 300) return Results.BadRequest();
            try
            {
                return Results.Ok(await agent.Search(title.Trim(), cancellationToken));
            }
            catch (MetadataLookupException error)
            {
                return Results.Json(new { message = error.Message }, statusCode: StatusCodes.Status502BadGateway);
            }
        });
        series.MapPut("/metadata", async (Guid root, string seriesKey, MetadataChange change, LibraryStore store,
            MetadataAgent agent, HttpContext context, IAntiforgery antiforgery, CancellationToken cancellationToken) =>
        {
            if (!await antiforgery.IsRequestValidAsync(context)) return Results.BadRequest();
            if (!LibraryEndpoints.IsSeriesKey(seriesKey) || !store.TryGetRoot(root, out _)) return Results.NotFound();
            if (change.Kind is not (null or "manga" or "doujin")) return Results.BadRequest();

            SeriesMetadata metadata;
            if (change.ProviderId is { } providerId)
            {
                if (providerId <= 0) return Results.BadRequest();
                try
                {
                    var match = await agent.Read(providerId, cancellationToken);
                    if (match is null) return Results.NotFound();
                    metadata = new(match.Title, change.Kind ?? match.Kind, match.AlternateTitle, match.Summary, match.Status,
                        match.TotalChapters, match.TotalVolumes, match.ProviderId);
                }
                catch (MetadataLookupException error)
                {
                    return Results.Json(new { message = error.Message }, statusCode: StatusCodes.Status502BadGateway);
                }
            }
            else
            {
                var title = change.Title?.Trim();
                if (string.IsNullOrEmpty(title) || title.Length > 300 || change.AlternateTitle is { Length: > 300 } ||
                    change.Summary is { Length: > 6000 } || change.Status is { Length: > 40 } ||
                    change.TotalChapters is < 1 or > 100_000 || change.TotalVolumes is < 1 or > 100_000)
                {
                    return Results.BadRequest(new { message = "Enter a title and valid totals before saving metadata." });
                }
                metadata = new(title, change.Kind ?? "manga", EmptyAsNull(change.AlternateTitle), EmptyAsNull(change.Summary),
                    EmptyAsNull(change.Status), change.TotalChapters, change.TotalVolumes, null);
            }

            return store.SaveMetadata(root, seriesKey.ToLowerInvariant(), metadata) ? Results.Ok(metadata) : Results.NotFound();
        }).WithMetadata(new RequestSizeLimitAttribute(32 * 1024));

        series.MapPut("/coverage", async (Guid root, string seriesKey, CoverageChange change, LibraryStore store,
            HttpContext context, IAntiforgery antiforgery) =>
        {
            if (!await antiforgery.IsRequestValidAsync(context)) return Results.BadRequest();
            if (!LibraryEndpoints.IsSeriesKey(seriesKey)) return Results.NotFound();

            var volume = change.Volume?.Trim() ?? "";
            if (!IsContentId(change.ContentId) ||
                (volume.Length > 0 && !BookNumbers.IsNumber(volume)) || change.Chapters is not { Length: <= 500 } chapters ||
                chapters.Any(chapter => chapter is null || !BookNumbers.IsNumber(chapter)))
            {
                return Results.BadRequest(new { message = "Use individual numeric chapter labels, such as 1, 1.5, and 2." });
            }

            var distinct = chapters.Select(BookNumbers.Canonical).Distinct(StringComparer.Ordinal).ToArray();
            return store.SaveCoverage(root, seriesKey.ToLowerInvariant(), change.ContentId!.ToLowerInvariant(),
                volume.Length == 0 ? "" : BookNumbers.Canonical(volume), distinct) ? Results.NoContent() : Results.NotFound();
        }).WithMetadata(new RequestSizeLimitAttribute(16 * 1024));
    }

    private static string? EmptyAsNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static bool IsContentId(string? value) => value is { Length: 32 } && value.All(char.IsAsciiHexDigit);
}
