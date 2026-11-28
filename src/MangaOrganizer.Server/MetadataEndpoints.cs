using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;

namespace MangaOrganizer.Server;

internal sealed record MetadataChange(int? ProviderId, string? Title, string? Kind, string? AlternateTitle, string? Summary,
    string? Status, int? TotalChapters, int? TotalVolumes, bool UseEditedFields = false, SeriesTag?[]? Tags = null, bool UseProviderTags = false,
    bool RemoveAniListLink = false, int? ExpectedAniListId = null, SeriesFileReview[]? ReviewFiles = null, string?[]? AlternateTitles = null,
    string? SummaryMarkup = null);
internal sealed record CoverageChange(string? ContentId, string? Volume, string[]? Chapters, bool WholeVolume = false);
internal sealed record FileCoverageChange(string? FileId, string? ContentId, string? SeriesKey, string? Revision, string? Volume, string[]? Chapters);
internal sealed record FileCoverageBatch(FileCoverageChange?[]? Changes);
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
                    change.Summary is { Length: > 6000 } || change.SummaryMarkup is { Length: > 24000 } || change.Status is { Length: > 40 } ||
                    change.TotalChapters is < 1 or > 100_000 || change.TotalVolumes is < 1 or > 100_000)
                {
                    return Results.BadRequest(new { message = "Enter a title and valid totals before saving metadata." });
                }
                var alternates = change.AlternateTitles ?? (EmptyAsNull(change.AlternateTitle) is { } alternate ? [alternate] : []);
                if (alternates.Length > 32 || alternates.Any(value => value?.Trim() is not { Length: > 0 and <= 300 } name || name.Any(char.IsControl)))
                    return Results.BadRequest(new { message = "Enter up to 32 alternate titles with 1–300 characters each." });

                var distinct = alternates.Select(value => value!.Trim())
                    .Where(value => !string.Equals(value, title, StringComparison.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                edited = new(title, change.Kind ?? "manga", distinct.FirstOrDefault(), EmptyAsNull(change.Summary),
                    EmptyAsNull(change.Status), change.TotalChapters, change.TotalVolumes, null, null)
                {
                    AlternateTitles = distinct,
                    SummaryMarkup = EmptyAsNull(change.SummaryMarkup),
                };
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

            if (!IsContentId(change.ContentId) || NormalizeCoverage(change.Volume, change.Chapters) is not { } labels ||
                change.WholeVolume && (labels.Volume.Length == 0 || labels.Chapters.Length != 0))
            {
                return Results.BadRequest(new { message = "Use individual numeric chapter labels, such as 1, 1.5, and 2." });
            }

            return store.SaveCoverage(root, seriesKey.ToLowerInvariant(), change.ContentId!.ToLowerInvariant(),
                labels.Volume, labels.Chapters, change.WholeVolume, published == true) ? Results.NoContent() : Results.NotFound();
        }).WithMetadata(new RequestSizeLimitAttribute(16 * 1024));
    }

    public static void MapFileCoverage(this RouteGroupBuilder library)
    {
        library.MapPut("/{root:guid}/file-coverage", async (Guid root, bool? published, FileCoverageBatch batch, LibraryStore store,
            HttpContext context, IAntiforgery antiforgery) =>
        {
            if (!await antiforgery.IsRequestValidAsync(context) || batch.Changes is not { Length: > 0 and <= 500 } changes)
                return Results.BadRequest();

            var edits = new List<FileCoverageEdit>(changes.Length);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var change in changes)
            {
                if (change is null || !IsContentId(change.FileId) || !IsContentId(change.ContentId) ||
                    change.SeriesKey is null || !LibraryEndpoints.IsSeriesKey(change.SeriesKey) ||
                    change.Revision is not { Length: 64 } revision || !revision.All(char.IsAsciiHexDigit) ||
                    !seen.Add(change.ContentId!))
                    return Results.BadRequest();
                if (NormalizeCoverage(change.Volume, change.Chapters) is not { } labels)
                    return Results.BadRequest(new { message = "Use individual numeric chapter labels, such as 1, 1.5, and 2." });

                edits.Add(new(change.FileId!.ToLowerInvariant(), change.ContentId!.ToLowerInvariant(), change.SeriesKey.ToLowerInvariant(),
                    revision.ToLowerInvariant(), labels.Volume, labels.Chapters));
            }
            return store.SaveFileCoverage(root, edits, published == true) ? Results.NoContent() :
                Results.Conflict(new { message = "A release or its coverage changed. Refresh the file list and review the batch again." });
        }).WithMetadata(new RequestSizeLimitAttribute(8 * 1024 * 1024));
    }

    private static (string Volume, string[] Chapters)? NormalizeCoverage(string? input, string[]? chapters)
    {
        var volume = input?.Trim() ?? "";
        if (volume.Length > 0 && !BookNumbers.IsNumber(volume) || chapters is not { Length: <= 500 } ||
            chapters.Any(chapter => chapter is null || !BookNumbers.IsNumber(chapter))) return null;

        return (volume.Length == 0 ? "" : BookNumbers.Canonical(volume),
            chapters.Select(BookNumbers.Canonical).Distinct(StringComparer.Ordinal).ToArray());
    }

    private static string? EmptyAsNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static bool IsContentId(string? value) => value is { Length: 32 } && value.All(char.IsAsciiHexDigit);
}
