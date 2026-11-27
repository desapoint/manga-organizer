using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;

namespace MangaOrganizer.Server;

internal sealed record ChapterCompletion(string? Volume, string? Chapter, bool Completed);

internal sealed record ChapterPreference(string? Volume, string? Chapter, string? ContentId);

internal static class ChapterEditionEndpoints
{
    public static void MapChapterEditions(this RouteGroupBuilder library)
    {
        var series = library.MapGroup("/{root:guid}/series/{seriesKey}");
        series.MapGet("/chapter-releases", (Guid root, string seriesKey, string? volume, string? chapter, bool? published, LibraryStore store, HttpContext context) =>
            ReaderEndpoints.TryOwner(context, out var owner) && IsChapter(seriesKey, volume, chapter)
            && store.ReadChapterReleases(root, seriesKey.ToLowerInvariant(), NormalizeVolume(volume), BookNumbers.Canonical(chapter!), owner, published == true) is { } list
                ? Results.Ok(list) : Results.NotFound());

        series.MapGet("/volume-releases", (Guid root, string seriesKey, string? volume, bool? published, LibraryStore store, HttpContext context) =>
            ReaderEndpoints.TryOwner(context, out var owner) && LibraryEndpoints.IsSeriesKey(seriesKey)
            && volume is not null && BookNumbers.IsNumber(volume)
            && store.ReadVolumeReleases(root, seriesKey.ToLowerInvariant(), BookNumbers.Canonical(volume), owner, published == true) is { } list
                ? Results.Ok(list) : Results.NotFound());

        series.MapPut("/chapter-progress", async (Guid root, string seriesKey, ChapterCompletion change, LibraryStore store,
            HttpContext context, IAntiforgery antiforgery) =>
        {
            if (!await antiforgery.IsRequestValidAsync(context) || !IsChapter(seriesKey, change.Volume, change.Chapter))
                return Results.BadRequest();
            if (!ReaderEndpoints.TryOwner(context, out var owner)) return Results.Unauthorized();
            return store.SaveChapterCompletion(root, seriesKey.ToLowerInvariant(), NormalizeVolume(change.Volume),
                BookNumbers.Canonical(change.Chapter!), owner, change.Completed) ? Results.NoContent() : Results.NotFound();
        }).WithMetadata(new RequestSizeLimitAttribute(4096));

        series.MapPut("/chapter-preference", async (Guid root, string seriesKey, ChapterPreference preference, LibraryStore store,
            HttpContext context, IAntiforgery antiforgery) =>
        {
            if (!await antiforgery.IsRequestValidAsync(context) || !IsChapter(seriesKey, preference.Volume, preference.Chapter)
                || preference.ContentId is not null && !IsId(preference.ContentId))
                return Results.BadRequest();
            if (!ReaderEndpoints.TryOwner(context, out var owner)) return Results.Unauthorized();
            return store.SavePreferredChapterRelease(root, seriesKey.ToLowerInvariant(), NormalizeVolume(preference.Volume),
                BookNumbers.Canonical(preference.Chapter!), owner, preference.ContentId?.ToLowerInvariant())
                ? Results.NoContent() : Results.NotFound();
        }).WithMetadata(new RequestSizeLimitAttribute(4096));

        series.MapGet("/edition-volumes", (Guid root, string seriesKey, bool? published, LibraryStore store) =>
            LibraryEndpoints.IsSeriesKey(seriesKey) && store.ReadEditionVolumes(root, seriesKey.ToLowerInvariant(), published == true) is { } list
                ? Results.Ok(list) : Results.NotFound());
        series.MapGet("/edition-candidates", (Guid root, string seriesKey, string? volume, LibraryStore store) =>
            LibraryEndpoints.IsSeriesKey(seriesKey) && volume is not null && BookNumbers.IsNumber(volume)
            && store.ReadEditionCandidates(root, seriesKey.ToLowerInvariant(), BookNumbers.Canonical(volume)) is { } list
                ? Results.Ok(list) : Results.NotFound());

        series.MapPut("/edition-volumes", async (Guid root, string seriesKey, EditionVolumeChange change, LibraryStore store,
            HttpContext context, IAntiforgery antiforgery) =>
        {
            if (!await antiforgery.IsRequestValidAsync(context)) return Results.BadRequest();
            var edition = change.Edition?.Trim();
            if (!LibraryEndpoints.IsSeriesKey(seriesKey) || string.IsNullOrWhiteSpace(edition) || edition.Length > 120
                || change.Volume is null || !BookNumbers.IsNumber(change.Volume)
                || change.Chapters is not { Length: > 0 and <= 500 } chapters
                || chapters.Any(label => label is null || !BookNumbers.IsNumber(label))
                || change.ContentIds is not { Length: <= 500 } ids || ids.Any(id => !IsId(id))
                || change.FullVolumeContentId is { } full && (!IsId(full) || !ids.Contains(full, StringComparer.OrdinalIgnoreCase)))
                return Results.BadRequest(new { message = "Enter a verified volume and individual numeric chapter labels." });

            return store.SaveEditionManifest(root, seriesKey.ToLowerInvariant(), edition, BookNumbers.Canonical(change.Volume),
                chapters.Select(BookNumbers.Canonical).Distinct().ToArray(),
                ids.Select(id => id.ToLowerInvariant()).Distinct().ToArray(), change.FullVolumeContentId?.ToLowerInvariant())
                ? Results.NoContent() : Results.Conflict(new { message = "Check the indexed releases. A full-volume assignment requires an unresolved volume file." });
        }).WithMetadata(new RequestSizeLimitAttribute(40 * 1024));
    }

    private static bool IsChapter(string seriesKey, string? volume, string? chapter) => LibraryEndpoints.IsSeriesKey(seriesKey)
        && (string.IsNullOrEmpty(volume) || BookNumbers.IsNumber(volume)) && chapter is not null && BookNumbers.IsNumber(chapter);
    private static string NormalizeVolume(string? volume) => string.IsNullOrEmpty(volume) ? "" : BookNumbers.Canonical(volume);
    private static bool IsId(string? id) => id is { Length: 32 } && id.All(char.IsAsciiHexDigit);
}
