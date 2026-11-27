using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;

namespace MangaOrganizer.Server;

internal sealed record IntakeTargetChange(Guid? TargetRoot);
internal sealed record IntakeGroupChange(string[]? SeriesKeys, string? Title, string? TargetSeriesKey, bool Confirm, string? Kind);

internal static class LibraryEndpoints
{
    public static void MapLibrary(this RouteGroupBuilder api, string dataDirectory)
    {
        var library = api.MapGroup("/library/roots").RequireAuthorization();
        library.MapSeriesMetadata();
        library.MapChapterEditions();
        api.MapCollections();
        library.MapGet("", (LibraryStore store, LibraryScanQueue scans) => Results.Ok(store.ReadRoots()
            .Select(root => root.State is "queued" or "scanning" ? root with { ScanProgress = scans.Status(root.Id) } : root)));
        api.MapGet("/library/folders", (string? path) => FolderBrowser.Browse(path, dataDirectory)).RequireAuthorization();

        library.MapPost("", (LibraryFolderChange change, LibraryStore store, HttpContext context, IAntiforgery antiforgery) =>
            SaveFolder(null, change, store, context, antiforgery)).WithMetadata(new RequestSizeLimitAttribute(32 * 1024));
        library.MapPut("/{id:guid}", (Guid id, LibraryFolderChange change, LibraryStore store, HttpContext context, IAntiforgery antiforgery) =>
            SaveFolder(id, change, store, context, antiforgery)).WithMetadata(new RequestSizeLimitAttribute(32 * 1024));
        library.MapPut("/{id:guid}/intake-target", async (Guid id, IntakeTargetChange change, LibraryStore store,
            HttpContext context, IAntiforgery antiforgery) =>
        {
            if (!await antiforgery.IsRequestValidAsync(context)) return Results.BadRequest();
            try
            {
                return store.SetIntakeTarget(id, change.TargetRoot) ? Results.NoContent() : Results.NotFound();
            }
            catch (ArgumentException error)
            {
                return Results.BadRequest(new { message = error.Message });
            }
            catch (LibraryFolderBusyException error)
            {
                return Results.Conflict(new { message = error.Message });
            }
        }).WithMetadata(new RequestSizeLimitAttribute(4096));
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

        library.MapGet("/{id:guid}/files", (Guid id, int? offset, int? limit, string? seriesKey, string? search, string? volume, string? view, LibraryStore store) =>
        {
            if (!store.TryGetRoot(id, out _))
            {
                return Results.NotFound();
            }
            if (!IsValidPage(offset, limit) || !IsCatalogView(view) || (seriesKey is not null && !IsSeriesKey(seriesKey)) || search is { Length: > 160 } ||
                (volume is not null && (seriesKey is null || !BookNumbers.IsNumber(volume))))
            {
                return Results.BadRequest();
            }
            return Results.Ok(store.ReadFiles(id, offset ?? 0, limit ?? 50, seriesKey?.ToLowerInvariant(), search?.Trim() ?? "",
                volume is null ? null : BookNumbers.Canonical(volume), view));
        });

        library.MapGet("/{id:guid}/series", (Guid id, int? offset, int? limit, string? search, string? sort, bool? coverOnly, string? seriesKey, string? view, LibraryStore store, HttpContext context) =>
        {
            if (!store.TryGetRoot(id, out _))
            {
                return Results.NotFound();
            }

            var query = context.Request.Query;
            var filters = SeriesFilters.FromInput(search, coverOnly == true, query["includeTag"], query["excludeTag"]);
            return IsValidPage(offset, limit) && IsCatalogView(view) && filters is not null && sort is null or "title" or "recent" or "files" &&
                (seriesKey is null || IsSeriesKey(seriesKey))
                ? Results.Ok(store.ReadSeries(id, offset ?? 0, limit ?? 50, filters, sort, seriesKey?.ToLowerInvariant(), view: view))
                : Results.BadRequest();
        });

        library.MapPost("/{id:guid}/group-series", async (Guid id, IntakeGroupChange change, LibraryStore store,
            HttpContext context, IAntiforgery antiforgery) =>
        {
            var title = change.Title?.Trim();
            if (!await antiforgery.IsRequestValidAsync(context) || change.SeriesKeys is not { Length: >= 2 and <= 50 } keys
                || keys.Any(key => key is null || !IsSeriesKey(key)) || title is { Length: > 300 }
                || change.Kind is not (null or "manga" or "doujin"))
                return Results.BadRequest();
            var selected = keys.Select(key => key.ToLowerInvariant()).Distinct(StringComparer.Ordinal).ToArray();
            var target = change.TargetSeriesKey?.ToLowerInvariant();
            if (selected.Length != keys.Length || (target is null && string.IsNullOrEmpty(title))
                || (target is not null && !selected.Contains(target, StringComparer.Ordinal))) return Results.BadRequest();

            var error = store.GroupIntakeSeries(id, selected, title ?? "", target, change.Confirm, change.Kind ?? "manga");
            return error is null ? Results.NoContent() : Results.Conflict(new { message = error });
        }).WithMetadata(new RequestSizeLimitAttribute(8192));

        library.MapGet("/{id:guid}/tags", (Guid id, LibraryStore store) =>
            store.TryGetRoot(id, out _) ? Results.Ok(store.ReadSeriesTags(id)) : Results.NotFound());

        library.MapGet("/{id:guid}/series/{seriesKey}/cover", (Guid id, string seriesKey, LibraryStore store, HttpContext context) =>
            IsSeriesKey(seriesKey) ? CoverResult(context, store.ReadCover(id, seriesKey.ToLowerInvariant())) : Results.NotFound());

        library.MapGet("/{id:guid}/files/{fileId}/cover", (Guid id, string fileId, LibraryStore store, HttpContext context) =>
        {
            if (fileId.Length != 32 || !fileId.All(char.IsAsciiHexDigit)) return Results.NotFound();
            return CoverResult(context, store.ReadFileCover(id, fileId.ToLowerInvariant()));
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
            return scans.Queue(root) is { } queued ? Results.Accepted(value: queued) : Results.Conflict();
        });
    }

    internal static bool IsValidPage(int? offset, int? limit) => offset is not < 0 && limit is not (< 1 or > 100);
    internal static bool IsSeriesKey(string value) => value.Length == 64 && value.All(char.IsAsciiHexDigit);
    private static bool IsCatalogView(string? view) =>
        view is null or "all" or "library" or "attention" or "unassociated" or "association" or "series" or "metadata" or "cover" or "close";

    private static IResult CoverResult(HttpContext context, byte[]? cover)
    {
        if (cover is null) return Results.NotFound();
        context.Response.Headers.CacheControl = "private, max-age=120";
        context.Response.Headers.XContentTypeOptions = "nosniff";
        return Results.File(cover, CoverCache.MediaType);
    }

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
