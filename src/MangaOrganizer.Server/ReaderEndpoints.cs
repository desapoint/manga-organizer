using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;

namespace MangaOrganizer.Server;

internal static class ReaderEndpoints
{
    public static void MapReader(this RouteGroupBuilder api)
    {
        var reader = api.MapGroup("/reader").RequireAuthorization();

        reader.MapGet("/resume", (HttpContext context, LibraryStore store) =>
        {
            if (!TryOwner(context, out var owner)) return Results.Unauthorized();
            return store.ReadResume(owner) is { } resume ? Results.Ok(resume) : Results.NoContent();
        });

        reader.MapGet("/{contentId}", (string contentId, HttpContext context, LibraryStore store, BookReader books,
            CancellationToken cancellationToken) =>
        {
            if (!TryOwner(context, out var owner)) return Results.Unauthorized();
            if (!TryContentId(contentId, out var content)) return Results.NotFound();
            if (books.Open(content, cancellationToken) is not { } book) return Unavailable();

            return Results.Ok(new
            {
                book.Title,
                PageCount = book.Pages.Count,
                Progress = store.ReadProgress(owner, content),
            });
        });

        reader.MapGet("/{contentId}/pages/{index:int}", (string contentId, int index, HttpContext context, BookReader books,
            CancellationToken cancellationToken) =>
        {
            if (!TryContentId(contentId, out var content)) return Results.NotFound();
            if (books.Open(content, cancellationToken) is not { } book) return Unavailable();
            if (index < 0 || index >= book.Pages.Count) return Results.NotFound();
            if (books.Page(content, book.Pages[index], cancellationToken) is not { } page) return Unavailable();

            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            return Results.File(page.Bytes, page.MediaType);
        });

        reader.MapPut("/{contentId}/progress", async (string contentId, ReadingChange change, HttpContext context, IAntiforgery antiforgery,
            LibraryStore store, BookReader books, CancellationToken cancellationToken) =>
        {
            if (!TryOwner(context, out var owner)) return Results.Unauthorized();
            if (!await antiforgery.IsRequestValidAsync(context)) return Results.BadRequest();
            if (!TryContentId(contentId, out var content)) return Results.NotFound();
            if (change.UpdateId == Guid.Empty || change.BaseRevision < 0 || change.PageIndex < 0) return Results.BadRequest();
            if (books.Open(content, cancellationToken) is not { } book) return Unavailable();
            if (change.PageIndex >= book.Pages.Count) return Results.BadRequest();
            if (books.Page(content, book.Pages[change.PageIndex], cancellationToken) is null) return Unavailable();

            var result = store.SaveProgress(owner, content, change, book.Pages.Count);
            return result.Conflict ? Results.Conflict(result.Progress) : Results.Ok(result.Progress);
        }).WithMetadata(new RequestSizeLimitAttribute(4096));
    }

    internal static bool TryOwner(HttpContext context, out Guid owner) =>
        Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out owner);

    private static bool TryContentId(string value, out string content)
    {
        content = value.ToLowerInvariant();
        return value.Length == 32 && value.All(char.IsAsciiHexDigit);
    }

    private static IResult Unavailable() => Results.Json(new
    {
        message = "This file is unavailable, changed, or has no supported image pages. Check the file and rescan its folder.",
    }, statusCode: StatusCodes.Status422UnprocessableEntity);
}
