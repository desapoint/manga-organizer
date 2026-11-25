using Microsoft.AspNetCore.Antiforgery;

namespace MangaOrganizer.Server;

internal static class LibraryEndpoints
{
    public static void MapLibrary(this RouteGroupBuilder api)
    {
        var library = api.MapGroup("/library/roots").RequireAuthorization();
        library.MapGet("", (LibraryStore store) => Results.Ok(store.ReadRoots()));

        library.MapGet("/{id:guid}/files", (Guid id, int? offset, int? limit, LibraryStore store) =>
        {
            if (!store.TryGetRoot(id, out _))
            {
                return Results.NotFound();
            }
            if (offset is < 0 || limit is < 1 or > 100)
            {
                return Results.BadRequest();
            }
            return Results.Ok(store.ReadFiles(id, offset ?? 0, limit ?? 50));
        });

        library.MapPost("/{id:guid}/scan", async (Guid id, LibraryStore store, LibraryScanQueue scans,
            HttpContext context, IAntiforgery antiforgery) =>
        {
            if (!await antiforgery.IsRequestValidAsync(context))
            {
                return Results.BadRequest();
            }
            if (!store.TryGetRoot(id, out var root) || root is null)
            {
                return Results.NotFound();
            }
            return scans.TryQueue(root) ? Results.Accepted() : Results.Conflict();
        });
    }
}
