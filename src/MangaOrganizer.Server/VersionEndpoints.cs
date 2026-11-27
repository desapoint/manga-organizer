using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;

namespace MangaOrganizer.Server;

internal sealed record VersionQuery(Guid? Root, int? Offset, int? Limit, string? Search, string? Language,
    string? Color, string? Censorship, string? Edition, string? Group, bool? Reviewed);
internal sealed record ReleaseTagChange(string? ContentId, string? Language, string? Color, string? Censorship,
    string? Edition, string? Group, int Revision);
internal sealed record ReleaseTagSelection(string?[]? FileIds);
internal sealed record ReleaseTagTarget(string FileId, string ContentId, string RelativePath, int Revision);
internal sealed record ReleaseTagBatch(ReleaseTagTarget?[]? Targets, string? Field, string? Value);

internal static partial class VersionEndpoints
{
    public static void MapVersions(this RouteGroupBuilder api)
    {
        var versions = api.MapGroup("/versions").RequireAuthorization();
        versions.MapGet("/series", ([AsParameters] VersionQuery query, LibraryStore store) =>
            Filters(query) is { } filters ? Results.Ok(store.ReadVersionSeries(query.Root, filters, query.Offset ?? 0, query.Limit ?? 50))
                : Results.BadRequest());
        versions.MapGet("/{root:guid}/series/{key}", (Guid root, string key, [AsParameters] VersionQuery query, LibraryStore store) =>
            LibraryEndpoints.IsSeriesKey(key) && Filters(query) is { } filters
                ? Results.Ok(store.ReadVersionFiles(root, key.ToLowerInvariant(), filters, query.Offset ?? 0, query.Limit ?? 50))
                : Results.BadRequest());

        versions.MapGet("/{root:guid}/files/{fileId}", (Guid root, string fileId, LibraryStore store) =>
            IsFileId(fileId) && store.ReadStoredRelease(root, fileId.ToLowerInvariant()) is { } release
                ? Results.Ok(release) : Results.NotFound());
        versions.MapPut("/{root:guid}/files/{fileId}", async (Guid root, string fileId, ReleaseTagChange input, LibraryStore store,
            HttpContext context, IAntiforgery antiforgery) =>
        {
            if (!await antiforgery.IsRequestValidAsync(context) || !IsFileId(fileId) || !IsFileId(input.ContentId)
                || input.Revision < 0 || !ValidLanguage(input.Language) || !ValidColor(input.Color) || !ValidCensorship(input.Censorship)
                || !ValidText(input.Edition, 120) || !ValidText(input.Group, 120))
                return Results.BadRequest(new { message = "Check the language code and release tags." });

            var tags = new ReleaseTags(Clean(input.Language)?.ToLowerInvariant(), input.Color ?? "unknown", input.Censorship ?? "unknown",
                Clean(input.Edition), Clean(input.Group), input.Revision);
            return store.SaveReleaseTags(root, fileId.ToLowerInvariant(), input.ContentId!.ToLowerInvariant(), tags)
                ? Results.NoContent()
                : Results.Conflict(new { message = "This file or its tags changed. Reopen the editor to review the latest values." });
        }).WithMetadata(new RequestSizeLimitAttribute(4096));

        versions.MapPost("/{root:guid}/batch-preview", async (Guid root, ReleaseTagSelection input, LibraryStore store,
            HttpContext context, IAntiforgery antiforgery) =>
        {
            if (!await antiforgery.IsRequestValidAsync(context) || input.FileIds is not { Length: >= 1 and <= 50 }
                || !input.FileIds.All(IsFileId) || input.FileIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() != input.FileIds.Length)
                return Results.BadRequest();

            var files = store.ReadReleaseTagsBatch(root, input.FileIds.Select(id => id!.ToLowerInvariant()).ToArray());
            return files is not null ? Results.Ok(files) : BatchConflict();
        }).WithMetadata(new RequestSizeLimitAttribute(8192));

        versions.MapPut("/{root:guid}/batch", async (Guid root, ReleaseTagBatch input, LibraryStore store,
            HttpContext context, IAntiforgery antiforgery) =>
        {
            if (!await antiforgery.IsRequestValidAsync(context) || input.Targets is not { Length: >= 1 and <= 50 }
                || !input.Targets.All(target => target is not null && IsFileId(target.FileId) && IsFileId(target.ContentId)
                    && !string.IsNullOrEmpty(target.RelativePath) && target.Revision >= 0)
                || input.Targets.Select(target => target!.FileId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != input.Targets.Length
                || !ValidBatchValue(input.Field, input.Value))
                return Results.BadRequest(new { message = "Check the selected files and the release tag value." });

            var targets = input.Targets.Select(target => new ReleaseTagTarget(
                target!.FileId.ToLowerInvariant(), target.ContentId.ToLowerInvariant(), target.RelativePath, target.Revision)).ToArray();
            return store.SaveReleaseTagsBatch(root, targets, input.Field!, Clean(input.Value))
                ? Results.NoContent() : BatchConflict();
        }).WithMetadata(new RequestSizeLimitAttribute(2 * 1024 * 1024));
    }

    private static IResult BatchConflict() =>
        Results.Conflict(new { message = "A selected file or its tags changed. Close the editor and review the selection again." });

    private static bool ValidBatchValue(string? field, string? value) => field switch
    {
        "language" => ValidText(value, 35) && ValidLanguage(value),
        "color" => ValidColor(value),
        "censorship" => ValidCensorship(value),
        "edition" or "group" => ValidText(value, 120),
        _ => false,
    };

    private static VersionFilters? Filters(VersionQuery query) =>
        LibraryEndpoints.IsValidPage(query.Offset, query.Limit) && ValidText(query.Search, 160) && ValidLanguage(query.Language)
            && ValidColor(query.Color) && ValidCensorship(query.Censorship) && ValidText(query.Edition, 120) && ValidText(query.Group, 120)
            ? new(query.Search?.Trim() ?? "", Clean(query.Language)?.ToLowerInvariant(), query.Color, query.Censorship,
                Clean(query.Edition), Clean(query.Group), query.Reviewed)
            : null;

    private static bool ValidColor(string? value) => value is null or "unknown" or "black-and-white" or "full-color" or "partial";
    private static bool ValidCensorship(string? value) => value is null or "unknown" or "uncensored" or "censored" or "mixed";
    private static bool ValidText(string? value, int max) => value is null || value.Length <= max && !value.Any(char.IsControl);
    private static bool ValidLanguage(string? value) => Clean(value) is not { } language || language.Length <= 35 && LanguageCode().IsMatch(language);
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static bool IsFileId(string? value) => value is { Length: 32 } && value.All(char.IsAsciiHexDigit);

    [GeneratedRegex("^[A-Za-z]{2,8}(?:-[A-Za-z0-9]{1,8})*$", RegexOptions.CultureInvariant)]
    private static partial Regex LanguageCode();
}
