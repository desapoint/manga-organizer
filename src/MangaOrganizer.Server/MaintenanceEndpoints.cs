using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;

namespace MangaOrganizer.Server;

internal sealed record MaintenanceScans(bool AllLibraries, Guid[]? RootIds);
internal sealed record MaintenanceScanResult(Guid RootId, string State);
internal sealed record ResetPreviewRequest(string? Level, Guid[]? RootIds);
internal sealed record ResetConfirmation(string? Token, string? Confirmation);
internal sealed record AuthorizedReset(Guid Owner, ResetScope Scope, string Fingerprint);

internal static class MaintenanceEndpoints
{
    public static void MapMaintenance(this RouteGroupBuilder api)
    {
        var maintenance = api.MapGroup("/maintenance").RequireAuthorization();
        MapResets(maintenance);
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

    private static void MapResets(RouteGroupBuilder maintenance)
    {
        var resets = maintenance.MapGroup("/resets");
        resets.MapGet("", (LibraryStore store, HttpContext context) =>
            ReaderEndpoints.TryOwner(context, out var owner) ? Results.Ok(store.ReadResets(owner)) : Results.Unauthorized());

        resets.MapPost("/preview", async (ResetPreviewRequest request, LibraryStore store, HttpContext context,
            IAntiforgery antiforgery, IDataProtectionProvider protection) =>
        {
            if (!ReaderEndpoints.TryOwner(context, out var owner)) return Results.Unauthorized();
            if (!await antiforgery.IsRequestValidAsync(context) || NormalizeReset(request) is not { } scope)
                return Results.BadRequest();
            try
            {
                var plan = store.PreviewReset(owner, scope);
                var token = ResetProtector(protection).Protect(JsonSerializer.Serialize(new AuthorizedReset(owner, scope, plan.Fingerprint)),
                    TimeSpan.FromMinutes(10));
                return Results.Ok(new { scope.Level, scope.RootIds, plan.Affected, Token = token, Confirmation = ResetPhrase(scope.Level) });
            }
            catch (MaintenanceResetException error)
            {
                return Results.Conflict(new { message = error.Message });
            }
        }).WithMetadata(new RequestSizeLimitAttribute(16 * 1024));

        resets.MapPost("/apply", async (ResetConfirmation request, LibraryStore store, HttpContext context,
            IAntiforgery antiforgery, IDataProtectionProvider protection, ILogger<LibraryStore> logger) =>
        {
            if (!ReaderEndpoints.TryOwner(context, out var owner)) return Results.Unauthorized();
            if (!await antiforgery.IsRequestValidAsync(context) || request.Token is not { Length: > 0 and <= 24576 } token)
                return Results.BadRequest();
            AuthorizedReset? plan;
            try
            {
                plan = JsonSerializer.Deserialize<AuthorizedReset>(ResetProtector(protection).Unprotect(token));
            }
            catch (Exception error) when (error is CryptographicException or JsonException)
            {
                return Results.Conflict(new { message = "The reset preview expired or changed. Preview it again." });
            }
            if (plan is null || plan.Owner != owner || request.Confirmation != ResetPhrase(plan.Scope.Level))
                return Results.BadRequest(new { message = "Enter the confirmation shown in the reset preview." });
            try
            {
                return Results.Ok(store.ApplyReset(owner, plan.Scope, plan.Fingerprint));
            }
            catch (MaintenanceResetException error)
            {
                return Results.Conflict(new { message = error.Message });
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or SqliteException or InvalidOperationException)
            {
                logger.LogWarning("Maintenance reset stopped before commit ({ErrorType}).", error.GetType().Name);
                return Results.Json(new { message = "The reset stopped before commit. Check the server data folder and try a fresh preview." },
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        }).WithMetadata(new RequestSizeLimitAttribute(32 * 1024));
    }

    private static ResetScope? NormalizeReset(ResetPreviewRequest request) => request switch
    {
        { Level: "progress", RootIds: null or { Length: 0 } } => new("progress", []),
        { Level: "catalog" or "configuration", RootIds: { Length: > 0 and <= 200 } ids } when ids.All(id => id != Guid.Empty) =>
            new(request.Level!, ids.Distinct().Order().ToArray()),
        _ => null,
    };

    private static string ResetPhrase(string level) => $"RESET {level.ToUpperInvariant()}";
    private static ITimeLimitedDataProtector ResetProtector(IDataProtectionProvider provider) =>
        provider.CreateProtector("MangaOrganizer.MaintenanceReset.v1").ToTimeLimitedDataProtector();
}
