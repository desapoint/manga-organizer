using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;

namespace MangaOrganizer.Server;

internal sealed record AssociationChange(string? ContentId, string? SeriesKey, string? MetadataId, string? Revision);

internal static class AssociationEndpoints
{
    public static void MapFileAssociations(this RouteGroupBuilder api)
    {
        var associations = api.MapGroup("/file-associations").RequireAuthorization();
        associations.MapGet("/series", (string? search, int? offset, int? limit, LibraryStore store) =>
            LibraryEndpoints.IsValidPage(offset, limit) && search is not { Length: > 160 }
                ? Results.Ok(store.ReadAssociationSeries(search?.Trim() ?? "", offset ?? 0, limit ?? 50))
                : Results.BadRequest());

        associations.MapPost("/{root:guid}/files/{fileId}/preview", async (Guid root, string fileId, AssociationChange change,
            HttpContext context, IAntiforgery antiforgery, LibraryStore store) =>
        {
            if (!await antiforgery.IsRequestValidAsync(context) || Parse(root, fileId, change) is not { } scope)
                return Results.BadRequest();
            return store.PreviewAssociation(scope) is { } preview ? Results.Ok(preview) :
                Results.Conflict(new { message = "The file changed, or the selected series conflicts with metadata in an affected folder." });
        }).WithMetadata(new RequestSizeLimitAttribute(4096));

        associations.MapPut("/{root:guid}/files/{fileId}", async (Guid root, string fileId, AssociationChange change,
            HttpContext context, IAntiforgery antiforgery, LibraryStore store) =>
        {
            if (!await antiforgery.IsRequestValidAsync(context) || Parse(root, fileId, change) is not { } scope
                || change.Revision is not { Length: 64 } revision || !revision.All(char.IsAsciiHexDigit))
                return Results.BadRequest();
            return store.ApplyAssociation(scope, revision.ToLowerInvariant()) ? Results.NoContent() :
                Results.Conflict(new { message = "The preview changed or the server is processing files. Wait for scans and file operations, then preview again." });
        }).WithMetadata(new RequestSizeLimitAttribute(4096));
    }

    private static AssociationScope? Parse(Guid root, string fileId, AssociationChange change) =>
        IsId(fileId) && IsId(change.ContentId) && IsId(change.MetadataId)
            && change.SeriesKey is { } series && LibraryEndpoints.IsSeriesKey(series)
            ? new(root, fileId.ToLowerInvariant(), change.ContentId!.ToLowerInvariant(), series.ToLowerInvariant(),
                change.MetadataId!.ToLowerInvariant())
            : null;

    private static bool IsId(string? id) => id is { Length: 32 } && id.All(char.IsAsciiHexDigit);
}
