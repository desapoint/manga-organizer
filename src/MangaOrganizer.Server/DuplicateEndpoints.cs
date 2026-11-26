using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;

namespace MangaOrganizer.Server;

internal static class DuplicateEndpoints
{
    public static void MapDuplicateReview(this RouteGroupBuilder operations)
    {
        operations.MapGet("/duplicates/{root:guid}/series/{series}", (Guid root, string series, LibraryStore store) =>
            LibraryEndpoints.IsSeriesKey(series) && store.ReadSeriesDuplicateReview(root, series.ToLowerInvariant()) is { } review
                ? Results.Ok(review) : Results.NotFound());

        operations.MapGet("/duplicates/{root:guid}/{fileId}", (Guid root, string fileId, ManagedFileOperations manager) =>
            IsFileId(fileId) && manager.ReviewDuplicates(root, fileId.ToLowerInvariant()) is { } comparison ? Results.Ok(comparison) : Results.NotFound());

        operations.MapGet("/duplicates/{root:guid}/{fileId}/archive", (Guid root, string fileId, ManagedFileOperations manager) =>
            IsFileId(fileId) && manager.ReviewArchive(root, fileId.ToLowerInvariant()) is { } evidence ? Results.Ok(evidence) : Results.NotFound());

        operations.MapPost("/quarantine-preview", async (QuarantineRequest request, ManagedFileOperations manager,
            HttpContext context, IAntiforgery antiforgery) =>
        {
            if (!await antiforgery.IsRequestValidAsync(context) || !IsFileId(request.SourceFile) || !IsFileId(request.KeepFile))
                return Results.BadRequest();
            try
            {
                return Results.Ok(manager.PreviewQuarantine(request.RootId, request.SourceFile!.ToLowerInvariant(), request.KeepFile!.ToLowerInvariant()));
            }
            catch (FileOperationException error)
            {
                return Results.Conflict(new { message = error.Message });
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return Results.Conflict(new { message = "The source or retained file could not be verified." });
            }
        }).WithMetadata(new RequestSizeLimitAttribute(4096));
    }

    private static bool IsFileId(string? id) => id is { Length: 32 } && id.All(char.IsAsciiHexDigit);
}
