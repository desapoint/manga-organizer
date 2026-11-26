using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;

namespace MangaOrganizer.Server;

internal static class LibraryEndpoints
{
    public static void MapLibrary(this RouteGroupBuilder api, string dataDirectory)
    {
        var library = api.MapGroup("/library/roots").RequireAuthorization();
        library.MapGet("", (LibraryStore store) => Results.Ok(store.ReadRoots()));
        api.MapGet("/library/folders", (string? path) => FolderBrowser.Browse(path, dataDirectory)).RequireAuthorization();

        library.MapPost("", (LibraryFolderChange change, LibraryStore store, HttpContext context, IAntiforgery antiforgery) =>
            SaveFolder(null, change, store, context, antiforgery)).WithMetadata(new RequestSizeLimitAttribute(32 * 1024));
        library.MapPut("/{id:guid}", (Guid id, LibraryFolderChange change, LibraryStore store, HttpContext context, IAntiforgery antiforgery) =>
            SaveFolder(id, change, store, context, antiforgery)).WithMetadata(new RequestSizeLimitAttribute(32 * 1024));
        library.MapDelete("/{id:guid}", async (Guid id, LibraryStore store, HttpContext context, IAntiforgery antiforgery) =>
        {
            if (!await antiforgery.IsRequestValidAsync(context))
            {
                return Results.BadRequest();
            }
            try
            {
                return store.RemoveFolder(id) ? Results.NoContent() : Results.NotFound();
            }
            catch (LibraryFolderBusyException error)
            {
                return Results.Conflict(new { message = error.Message });
            }
        });

        library.MapGet("/{id:guid}/files", (Guid id, int? offset, int? limit, string? seriesKey, LibraryStore store) =>
        {
            if (!store.TryGetRoot(id, out _))
            {
                return Results.NotFound();
            }
            if (!IsValidPage(offset, limit) || (seriesKey is not null && (seriesKey.Length != 64 || !seriesKey.All(char.IsAsciiHexDigit))))
            {
                return Results.BadRequest();
            }
            return Results.Ok(store.ReadFiles(id, offset ?? 0, limit ?? 50, seriesKey?.ToLowerInvariant()));
        });

        library.MapGet("/{id:guid}/series", (Guid id, int? offset, int? limit, LibraryStore store) =>
        {
            if (!store.TryGetRoot(id, out _))
            {
                return Results.NotFound();
            }

            return IsValidPage(offset, limit)
                ? Results.Ok(store.ReadSeries(id, offset ?? 0, limit ?? 50))
                : Results.BadRequest();
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

    private static bool IsValidPage(int? offset, int? limit) => offset is not < 0 && limit is not (< 1 or > 100);

    private static async Task<IResult> SaveFolder(Guid? id, LibraryFolderChange change, LibraryStore store,
        HttpContext context, IAntiforgery antiforgery)
    {
        if (!await antiforgery.IsRequestValidAsync(context))
        {
            return Results.BadRequest();
        }
        try
        {
            var saved = store.SaveFolder(id, change);
            return saved is null ? Results.NotFound() : Results.Ok(saved);
        }
        catch (LibraryFolderBusyException error)
        {
            return Results.Conflict(new { message = error.Message });
        }
        catch (ArgumentException error)
        {
            return Results.BadRequest(new { message = error.Message });
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return Results.BadRequest(new { message = "Choose an existing folder on this server that Manga Organizer can access." });
        }
    }
}
