using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;

namespace MangaOrganizer.Server;

internal sealed record MaintenanceScans(bool AllLibraries, Guid[]? RootIds);
internal sealed record MaintenanceScanResult(Guid RootId, string State);

internal static class MaintenanceEndpoints
{
    public static void MapMaintenance(this RouteGroupBuilder api)
    {
        var maintenance = api.MapGroup("/maintenance").RequireAuthorization();
        maintenance.MapPost("/scans", async (MaintenanceScans request, LibraryStore store, LibraryScanQueue scans,
            HttpContext context, IAntiforgery antiforgery) =>
        {
            if (!await antiforgery.IsRequestValidAsync(context) ||
                (request.AllLibraries ? request.RootIds is { Length: > 0 } : request.RootIds is not { Length: > 0 and <= 500 }))
                return Results.BadRequest();

            var libraries = store.ReadRoots().Where(root => root.Kind is "library" or "both").ToDictionary(root => root.Id);
            var ids = request.AllLibraries ? libraries.Keys.ToArray() : request.RootIds!.Distinct().ToArray();
            var results = new List<MaintenanceScanResult>(ids.Length);
            foreach (var id in ids)
            {
                var state = !libraries.TryGetValue(id, out var root) ? "missing" :
                    scans.Queue(new(root.Id, root.Path)) is not null ? "queued" : "busy";
                results.Add(new(id, state));
            }
            return Results.Accepted(value: results);
        }).WithMetadata(new RequestSizeLimitAttribute(24 * 1024));

        maintenance.MapPost("/artwork-cache/clear", async (LibraryStore store, HttpContext context, IAntiforgery antiforgery) =>
        {
            if (!await antiforgery.IsRequestValidAsync(context)) return Results.BadRequest();
            return store.ClearArtworkCache() is { } cleared ? Results.Ok(cleared) :
                Results.Conflict(new { message = "Wait until scans and file operations finish before clearing artwork." });
        }).WithMetadata(new RequestSizeLimitAttribute(1024));
    }
}
