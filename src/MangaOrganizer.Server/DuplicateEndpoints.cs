using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;

namespace MangaOrganizer.Server;

internal sealed record DuplicateDecisionChange(string? Fingerprint, long Latest, string? Decision, string? Note);

internal static class DuplicateEndpoints
{
    public static void MapDuplicateReview(this RouteGroupBuilder operations)
    {
        operations.MapGet("/duplicates/series", (Guid? root, string? kind, string? status, string? search, int? offset, int? limit, LibraryStore store) =>
            ValidQueue(kind, status, offset, limit) && (search?.Length ?? 0) <= 300
                ? Results.Ok(store.ReadDuplicateSeries(root, kind, status, search?.Trim() ?? "", offset ?? 0, limit ?? 50))
                : Results.BadRequest());

        operations.MapGet("/duplicates/{root:guid}/series/{series}/queue",
            (Guid root, string series, string? kind, string? status, int? offset, int? limit, LibraryStore store) =>
                LibraryEndpoints.IsSeriesKey(series) && ValidQueue(kind, status, offset, limit)
                    ? Results.Ok(store.ReadDuplicateQueue(root, series.ToLowerInvariant(), kind, status, offset ?? 0, limit ?? 50))
                    : Results.BadRequest());

        var decisions = operations.MapGroup("/duplicates/{root:guid}/series/{series}/decisions");
        decisions.MapGet("", (Guid root, string series, LibraryStore store) =>
            LibraryEndpoints.IsSeriesKey(series) && store.TryGetRoot(root, out _)
                ? Results.Ok(store.ReadDuplicateDecisions(root, series.ToLowerInvariant())) : Results.NotFound());
        decisions.MapPut("/{kind}", async (Guid root, string series, string kind, DuplicateDecisionChange change,
            LibraryStore store, HttpContext context, IAntiforgery antiforgery) =>
        {
            if (!ReaderEndpoints.TryOwner(context, out var owner)) return Results.Unauthorized();
            if (!await antiforgery.IsRequestValidAsync(context) || !LibraryEndpoints.IsSeriesKey(series) || !ValidKind(kind)
                || change.Fingerprint is not { Length: 64 } fingerprint || !fingerprint.All(char.IsAsciiHexDigit)
                || change.Latest < 0 || change.Decision is not ("keep" or "defer" or "reopen")
                || change.Note is { Length: > 400 } || change.Note?.Any(char.IsControl) == true)
                return Results.BadRequest();

            var note = string.IsNullOrWhiteSpace(change.Note) ? null : change.Note.Trim();
            return store.SaveDuplicateDecision(root, series.ToLowerInvariant(), kind, fingerprint.ToLowerInvariant(),
                change.Latest, change.Decision, note, owner)
                ? Results.NoContent()
                : Results.Conflict(new { message = "The review evidence or latest decision changed. Review the current series again." });
        }).WithMetadata(new RequestSizeLimitAttribute(4096));

        operations.MapGet("/duplicates/{root:guid}/series/{series}", (Guid root, string series, LibraryStore store) =>
            LibraryEndpoints.IsSeriesKey(series) && store.ReadSeriesDuplicateReview(root, series.ToLowerInvariant()) is { } review
                ? Results.Ok(review) : Results.NotFound());

        operations.MapGet("/duplicates/{root:guid}/series/{series}/folder-files", (Guid root, string series, LibraryStore store) =>
            LibraryEndpoints.IsSeriesKey(series) && store.ReadSeriesDuplicateReview(root, series.ToLowerInvariant(), compareFolders: true) is { } review
                ? Results.Ok(review.Files) : Results.NotFound());

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
                return Results.Ok(manager.PreviewQuarantine(request.RootId, request.SourceFile!.ToLowerInvariant(),
                    request.KeepRootId ?? request.RootId, request.KeepFile!.ToLowerInvariant()));
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

    private static bool ValidKind(string kind) => kind is "bytes" or "chapter" or "volume";

    private static bool ValidQueue(string? kind, string? status, int? offset, int? limit) =>
        (kind is null || ValidKind(kind)) && (status is null or "pending" or "kept" or "deferred")
        && LibraryEndpoints.IsValidPage(offset, limit);
}
