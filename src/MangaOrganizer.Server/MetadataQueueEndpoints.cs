using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;

namespace MangaOrganizer.Server;

internal sealed record MetadataQueueRefresh(MetadataQueueLocation[]? Items);
internal sealed record MetadataSuggestionConfirmation(string? Revision, int ProviderId);

internal static class MetadataQueueEndpoints
{
    public static void MapMetadataQueue(this RouteGroupBuilder api)
    {
        var queue = api.MapGroup("/metadata-suggestions").RequireAuthorization();
        queue.MapGet("", (Guid? root, string? search, int? offset, int? limit, LibraryStore store) =>
            LibraryEndpoints.IsValidPage(offset, limit) && (search?.Length ?? 0) <= 160
                ? Results.Ok(store.ReadMetadataQueue(root, search?.Trim() ?? "", offset ?? 0, limit ?? 50))
                : Results.BadRequest());

        queue.MapPost("/refresh", async (MetadataQueueRefresh request, LibraryStore store, HttpContext context, IAntiforgery antiforgery) =>
        {
            if (!await antiforgery.IsRequestValidAsync(context) || request.Items is not { Length: > 0 and <= 50 } items ||
                items.Any(item => item?.SeriesKey is not { } key || !LibraryEndpoints.IsSeriesKey(key)))
                return Results.BadRequest();
            var normalized = items.Select(item => item with { SeriesKey = item.SeriesKey.ToLowerInvariant() }).Distinct().ToArray();
            return Results.Accepted(value: new { queued = store.RefreshMetadataSuggestions(normalized) });
        }).WithMetadata(new RequestSizeLimitAttribute(16 * 1024));

        queue.MapPost("/{root:guid}/{series}", async (Guid root, string series, MetadataSuggestionConfirmation input,
            LibraryStore store, HttpContext context, IAntiforgery antiforgery) =>
        {
            if (!await antiforgery.IsRequestValidAsync(context) || !LibraryEndpoints.IsSeriesKey(series) ||
                input.Revision is not { Length: 32 } revision || !revision.All(char.IsAsciiHexDigit) || input.ProviderId <= 0)
                return Results.BadRequest(new { message = "Could not verify this confirmation. Refresh the page and try again." });
            try
            {
                var saved = store.ConfirmMetadataSuggestion(root, series.ToLowerInvariant(), revision.ToLowerInvariant(), input.ProviderId);
                return saved is not null ? Results.Ok(saved) :
                    Results.Conflict(new { message = "This match or series changed. Refresh the list and review the current suggestion." });
            }
            catch (SqliteException error) when (error.SqliteErrorCode is 5 or 6)
            {
                return Results.Json(new { message = "The catalog is finishing another update. Try confirming again in a moment." },
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        }).WithMetadata(new RequestSizeLimitAttribute(4096));
    }
}
