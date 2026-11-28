using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;

namespace MangaOrganizer.Server;

internal sealed record MetadataChange(int? ProviderId, string? Title, string? Kind, string? AlternateTitle, string? Summary,
    string? Status, int? TotalChapters, int? TotalVolumes, bool UseEditedFields = false, SeriesTag?[]? Tags = null, bool UseProviderTags = false,
    bool RemoveAniListLink = false, int? ExpectedAniListId = null, SeriesFileReview[]? ReviewFiles = null);
internal sealed record CoverageChange(string? ContentId, string? Volume, string[]? Chapters, bool WholeVolume = false);
internal sealed record AssociationConfirmation(string? FileId, string? ContentId);

internal static class MetadataEndpoints
{
    public static void MapSeriesMetadata(this RouteGroupBuilder library)
    {
        var series = library.MapGroup("/{root:guid}/series/{seriesKey}");
        series.MapGet("/details", (Guid root, string seriesKey, bool? published, LibraryStore store, HttpContext context) =>
        {
            if (!ReaderEndpoints.TryOwner(context, out var owner)) return Results.Unauthorized();
            return LibraryEndpoints.IsSeriesKey(seriesKey) && store.ReadSeriesDetails(root, seriesKey.ToLowerInvariant(), owner, published == true) is { } details
                ? Results.Ok(details) : Results.NotFound();
        });
        series.MapGet("/coverage", (Guid root, string seriesKey, string? contentId, bool? published, LibraryStore store) =>
            LibraryEndpoints.IsSeriesKey(seriesKey) && IsContentId(contentId) &&
                store.ReadCoverage(root, seriesKey.ToLowerInvariant(), contentId!.ToLowerInvariant(), published == true) is { } coverage
                ? Results.Ok(coverage) : Results.NotFound());
        series.MapPost("/association", async (Guid root, string seriesKey, AssociationConfirmation change, LibraryStore store,
            HttpContext context, IAntiforgery antiforgery) =>
        {
            if (!await antiforgery.IsRequestValidAsync(context) || !LibraryEndpoints.IsSeriesKey(seriesKey)
                || !IsContentId(change.FileId) || !IsContentId(change.ContentId)) return Results.BadRequest();
            return store.ConfirmFileAssociation(root, seriesKey.ToLowerInvariant(), change.FileId!.ToLowerInvariant(), change.ContentId!.ToLowerInvariant())
                ? Results.NoContent()
                : Results.Conflict(new { message = "The file or its series metadata changed. Refresh the inventory and review the association again." });
        }).WithMetadata(new RequestSizeLimitAttribute(4096));
        series.MapPost("/split-file", async (Guid root, string seriesKey, AssociationConfirmation change, LibraryStore store,
            HttpContext context, IAntiforgery antiforgery) =>
        {
            if (!await antiforgery.IsRequestValidAsync(context) || !LibraryEndpoints.IsSeriesKey(seriesKey) ||
                !IsContentId(change.FileId) || !IsContentId(change.ContentId)) return Results.BadRequest();
            var error = store.SplitIntakeFile(root, seriesKey.ToLowerInvariant(), change.FileId!.ToLowerInvariant(), change.ContentId!.ToLowerInvariant());
            return error is null ? Results.NoContent() : Results.Conflict(new { message = error });
        }).WithMetadata(new RequestSizeLimitAttribute(4096));
        series.MapGet("/metadata/search", async (Guid root, string seriesKey, string? title, LibraryStore store,
            MetadataAgent agent, CancellationToken cancellationToken) =>
        {
            if (!LibraryEndpoints.IsSeriesKey(seriesKey) || !store.TryGetRoot(root, out _)) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(title) || title.Length > 300) return Results.BadRequest();
            try
            {
                return Results.Ok((await agent.Search(title.Trim(), cancellationToken)).Select(match => match.ToMetadata()).ToArray());
            }
            catch (MetadataLookupException error)
            {
                return Results.Json(new { message = error.Message }, statusCode: StatusCodes.Status502BadGateway);
            }
        });
        series.MapGet("/metadata/anilist/{id:int:min(1)}", async (Guid root, string seriesKey, int id, LibraryStore store,
            MetadataAgent agent, CancellationToken cancellationToken) =>
        {
            if (!LibraryEndpoints.IsSeriesKey(seriesKey) || !store.TryGetRoot(root, out _)) return Results.NotFound();
            try
            {
                var match = await agent.Read(id, cancellationToken);
                return match is null ? Results.NotFound() : Results.Ok(match.ToMetadata());
            }
            catch (MetadataLookupException error)
            {
                return Results.Json(new { message = error.Message }, statusCode: StatusCodes.Status502BadGateway);
            }
        });
        series.MapPut("/metadata", async (Guid root, string seriesKey, bool? published, MetadataChange change, LibraryStore store,
            MetadataAgent agent, HttpContext context, IAntiforgery antiforgery, CancellationToken cancellationToken) =>
        {
            if (!await antiforgery.IsRequestValidAsync(context) || !SeriesFileReview.IsValid(change.ReviewFiles)) return Results.BadRequest();
            if (!LibraryEndpoints.IsSeriesKey(seriesKey) || !store.TryGetRoot(root, out _)) return Results.NotFound();
            if (change.Kind is not (null or "manga" or "doujin") ||
                change.RemoveAniListLink && (change.ProviderId is not null || change.UseProviderTags || change.ExpectedAniListId is <= 0) ||
                !change.RemoveAniListLink && change.ExpectedAniListId is not null)
                return Results.BadRequest();

            if (!SeriesTag.TryNormalize(change.Tags, out var tags) || change.UseProviderTags && tags is not null)
                return Results.BadRequest(new { message = SeriesTag.InvalidMessage });

            SeriesMetadata? edited = null;
            if (change.ProviderId is null || change.UseEditedFields)
            {
                var title = change.Title?.Trim();
                if (string.IsNullOrEmpty(title) || title.Length > 300 || change.AlternateTitle is { Length: > 300 } ||
                    change.Summary is { Length: > 6000 } || change.Status is { Length: > 40 } ||
                    change.TotalChapters is < 1 or > 100_000 || change.TotalVolumes is < 1 or > 100_000)
                {
                    return Results.BadRequest(new { message = "Enter a title and valid totals before saving metadata." });
                }
                edited = new(title, change.Kind ?? "manga", EmptyAsNull(change.AlternateTitle), EmptyAsNull(change.Summary),
                    EmptyAsNull(change.Status), change.TotalChapters, change.TotalVolumes, null, null);
            }

            SeriesMetadata metadata;
            var linkOnly = change.ProviderId is not null && !change.UseEditedFields;
            if (change.ProviderId is { } providerId)
            {
                if (providerId <= 0) return Results.BadRequest();
                var linked = linkOnly ? store.ReadLinkedProviderMetadata(providerId) : null;
                if (linked is not null)
                {
                    metadata = linked;
                }
                else try
                {
                    var match = await agent.Read(providerId, cancellationToken);
                    if (match is null) return Results.NotFound();
                    metadata = edited is null ? match.ToMetadata(change.Kind)
                        : edited with { ProviderId = match.ProviderId, ProviderDetails = match.ProviderDetails };
                }
                catch (MetadataLookupException error)
                {
                    return Results.Json(new { message = error.Message }, statusCode: StatusCodes.Status502BadGateway);
                }
            }
            else
            {
                metadata = edited!;
            }

            if (tags is not null) metadata = metadata with { ManualTags = tags };
            return store.SaveMetadata(root, seriesKey.ToLowerInvariant(), metadata, published == true, change.UseProviderTags,
                linkOnly, change.RemoveAniListLink, change.ExpectedAniListId, change.ReviewFiles) is { } saved
                ? Results.Ok(saved) : change.RemoveAniListLink
                    ? Results.Conflict(new { message = "The series or its AniList link changed. Reopen the editor to review the current link." })
                    : change.ReviewFiles is not null
                        ? Results.Conflict(new { message = "The reviewed files changed. Refresh the inventory and review the files again." })
                        : Results.NotFound();
        }).WithMetadata(new RequestSizeLimitAttribute(16 * 1024 * 1024));

        series.MapPut("/coverage", async (Guid root, string seriesKey, CoverageChange change, bool? published, LibraryStore store,
            HttpContext context, IAntiforgery antiforgery) =>
        {
            if (!await antiforgery.IsRequestValidAsync(context)) return Results.BadRequest();
            if (!LibraryEndpoints.IsSeriesKey(seriesKey)) return Results.NotFound();

            var volume = change.Volume?.Trim() ?? "";
            if (!IsContentId(change.ContentId) ||
                (volume.Length > 0 && !BookNumbers.IsNumber(volume)) || change.Chapters is not { Length: <= 500 } chapters ||
                chapters.Any(chapter => chapter is null || !BookNumbers.IsNumber(chapter)) ||
                change.WholeVolume && (volume.Length == 0 || chapters.Length != 0))
            {
                return Results.BadRequest(new { message = "Use individual numeric chapter labels, such as 1, 1.5, and 2." });
            }

            var distinct = chapters.Select(BookNumbers.Canonical).Distinct(StringComparer.Ordinal).ToArray();
            return store.SaveCoverage(root, seriesKey.ToLowerInvariant(), change.ContentId!.ToLowerInvariant(),
                volume.Length == 0 ? "" : BookNumbers.Canonical(volume), distinct, change.WholeVolume, published == true) ? Results.NoContent() : Results.NotFound();
        }).WithMetadata(new RequestSizeLimitAttribute(16 * 1024));
    }

    private static string? EmptyAsNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static bool IsContentId(string? value) => value is { Length: 32 } && value.All(char.IsAsciiHexDigit);
}
