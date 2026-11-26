using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;

namespace MangaOrganizer.Server;

internal static class FileOperationEndpoints
{
    public static void MapFileOperations(this RouteGroupBuilder api)
    {
        var operations = api.MapGroup("/file-operations").RequireAuthorization();
        operations.MapGet("", (ManagedFileOperations manager) => Results.Ok(manager.History()));
        operations.MapPost("/preview", async (FileOperationRequest request, ManagedFileOperations manager, HttpContext context, IAntiforgery antiforgery) =>
        {
            if (!await antiforgery.IsRequestValidAsync(context)) return Results.BadRequest();
            try
            {
                return Results.Ok(manager.Preview(request));
            }
            catch (FileOperationException error)
            {
                return Results.Conflict(new { message = error.Message });
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return Results.Conflict(new { message = "The source or destination folder could not be verified." });
            }
        }).WithMetadata(new RequestSizeLimitAttribute(16 * 1024));

        operations.MapPost("/{id:guid}/apply", (Guid id, ManagedFileOperations manager, HttpContext context, IAntiforgery antiforgery) =>
            Run(id, false, manager, context, antiforgery));
        operations.MapPost("/{id:guid}/restore", (Guid id, ManagedFileOperations manager, HttpContext context, IAntiforgery antiforgery) =>
            Run(id, true, manager, context, antiforgery));
    }

    private static async Task<IResult> Run(Guid id, bool restore, ManagedFileOperations manager, HttpContext context, IAntiforgery antiforgery)
    {
        if (!await antiforgery.IsRequestValidAsync(context)) return Results.BadRequest();
        try
        {
            var result = await manager.Run(id, restore, context.RequestAborted);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }
        catch (FileOperationException error)
        {
            return Results.Conflict(new { message = error.Message });
        }
    }
}
