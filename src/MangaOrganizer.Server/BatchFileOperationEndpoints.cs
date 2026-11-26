using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;

namespace MangaOrganizer.Server;

internal static class BatchFileOperationEndpoints
{
    public static void MapBatchFileOperations(this RouteGroupBuilder operations)
    {
        operations.MapPost("/batch-preview", async (BatchPreviewRequest request, ManagedFileOperations manager,
            HttpContext context, IAntiforgery antiforgery) =>
        {
            if (!await antiforgery.IsRequestValidAsync(context) ||
                request.Items is not { Length: > 0 and <= ManagedFileOperations.MaxBatchItems } items) return Results.BadRequest();
            return Results.Ok(manager.PreviewBatch(items));
        }).WithMetadata(new RequestSizeLimitAttribute(768 * 1024));

        operations.MapPost("/batch-apply", async (BatchApplyRequest request, ManagedFileOperations manager,
            HttpContext context, IAntiforgery antiforgery) =>
        {
            if (!await antiforgery.IsRequestValidAsync(context) ||
                request.Ids is not { Length: > 0 and <= ManagedFileOperations.MaxBatchItems } ids ||
                ids.Contains(Guid.Empty) || ids.Distinct().Count() != ids.Length) return Results.BadRequest();
            return Results.Ok(await manager.ApplyBatch(ids, context.RequestAborted));
        }).WithMetadata(new RequestSizeLimitAttribute(8192));
    }
}
