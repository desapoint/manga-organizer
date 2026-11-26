using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;

namespace MangaOrganizer.Server;

internal sealed record CollectionChange(string? Name, SeriesFilters? Filters);
internal sealed record CollectionMembership(Guid RootId, string? SeriesKey, bool Included);

internal static class CollectionEndpoints
{
    public static void MapCollections(this RouteGroupBuilder api)
    {
        var collections = api.MapGroup("/library/collections").RequireAuthorization();
        collections.MapGet("", (Guid? rootId, string? seriesKey, LibraryStore store) =>
            seriesKey is not null && !LibraryEndpoints.IsSeriesKey(seriesKey) ? Results.BadRequest() :
                Results.Ok(store.ReadCollections(rootId, seriesKey?.ToLowerInvariant())));
        collections.MapGet("/tags", (LibraryStore store) => Results.Ok(store.ReadSeriesTags(null)));
        collections.MapGet("/{id:guid}/series", (Guid id, int? offset, int? limit, bool? published, LibraryStore store) =>
        {
            if (!LibraryEndpoints.IsValidPage(offset, limit)) return Results.BadRequest();
            var collection = store.ReadCollections(id: id).SingleOrDefault();
            return collection is null ? Results.NotFound() : Results.Ok(store.ReadSeries(null, offset ?? 0, limit ?? 50,
                collection.Filters ?? SeriesFilters.Empty, manualCollection: collection.Filters is null ? id : null,
                view: published == true ? "library" : null));
        });

        collections.MapPost("", (CollectionChange change, LibraryStore store, HttpContext context, IAntiforgery antiforgery) =>
            Save(null, change, store, context, antiforgery)).WithMetadata(new RequestSizeLimitAttribute(32 * 1024));
        collections.MapPut("/{id:guid}", (Guid id, CollectionChange change, LibraryStore store, HttpContext context, IAntiforgery antiforgery) =>
            Save(id, change, store, context, antiforgery)).WithMetadata(new RequestSizeLimitAttribute(32 * 1024));
        collections.MapDelete("/{id:guid}", async (Guid id, LibraryStore store, HttpContext context, IAntiforgery antiforgery) =>
        {
            if (!await antiforgery.IsRequestValidAsync(context)) return Results.BadRequest();
            return store.RemoveCollection(id) ? Results.NoContent() : Results.NotFound();
        });
        collections.MapPut("/{id:guid}/membership", async (Guid id, CollectionMembership change, LibraryStore store,
            HttpContext context, IAntiforgery antiforgery) =>
        {
            if (!await antiforgery.IsRequestValidAsync(context) || change.SeriesKey is null ||
                !LibraryEndpoints.IsSeriesKey(change.SeriesKey) || change.RootId == Guid.Empty) return Results.BadRequest();
            return store.SetCollectionMembership(id, change.RootId, change.SeriesKey.ToLowerInvariant(), change.Included)
                ? Results.NoContent() : Results.NotFound();
        }).WithMetadata(new RequestSizeLimitAttribute(4096));
    }

    private static async Task<IResult> Save(Guid? id, CollectionChange change, LibraryStore store, HttpContext context, IAntiforgery antiforgery)
    {
        if (!await antiforgery.IsRequestValidAsync(context)) return Results.BadRequest();
        var name = change.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 100) return Results.BadRequest(new { message = "Enter a collection name." });
        var filters = change.Filters is { } f ? SeriesFilters.FromInput(f.Search, f.CoverOnly, f.IncludeTags ?? [], f.ExcludeTags ?? []) : null;
        if (change.Filters is not null && filters is null) return Results.BadRequest(new { message = "Review the collection filters." });
        try
        {
            var saved = store.SaveCollection(id, name, filters);
            return saved is null ? Results.NotFound() : Results.Ok(saved);
        }
        catch (SqliteException error) when (error.SqliteExtendedErrorCode == 2067)
        {
            return Results.Conflict(new { message = "A collection with this name already exists." });
        }
    }
}
