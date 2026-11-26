using System.Security.Claims;
using System.Threading.RateLimiting;
using MangaOrganizer.Server;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;

const string sessionScheme = CookieAuthenticationDefaults.AuthenticationScheme;
const string bootstrapPasswordSetting = "MANGA_ORGANIZER_BOOTSTRAP_PASSWORD";
var migrationOnly = args is ["--migrate"];
var builder = WebUi.CreateBuilder(migrationOnly ? [] : args);

var dataDirectory = Path.GetFullPath(builder.Configuration["MANGA_ORGANIZER_DATA_DIR"] ?? Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify),
    "MangaOrganizer"));
using var dataLease = AcquireDataLease(dataDirectory);
if (dataLease is null) return;
try
{
    var schemaVersion = DatabaseMigrations.Apply(dataDirectory);
    if (migrationOnly)
    {
        Console.WriteLine($"Database schema version {schemaVersion} is ready.");
        return;
    }
}
catch (Exception error) when (error is SqliteException or IOException or InvalidOperationException or UnauthorizedAccessException)
{
    Console.Error.WriteLine($"Database migration failed: {error.Message}");
    Environment.ExitCode = 1;
    return;
}

var bootstrapPassword = builder.Configuration[bootstrapPasswordSetting];
var (identity, _) = ServerStore.Open(dataDirectory, bootstrapPassword);
if (identity is null)
{
    if (!string.IsNullOrEmpty(bootstrapPassword) || !WebSetup.HasLoopbackUrls(builder.Configuration))
    {
        Console.Error.WriteLine($"For this server configuration, set {bootstrapPasswordSetting} to a password of 12–256 characters before first startup.");
        Environment.ExitCode = 1;
        return;
    }

    identity = await WebSetup.RunAsync(builder, dataDirectory, LibraryScanner.ReadRoots(builder.Configuration, dataDirectory));
    if (identity is null)
    {
        return;
    }
    builder = WebUi.CreateBuilder(args);
}

builder.Services.AddSingleton(new LibraryStore(dataDirectory, () =>
{
    var configuredPaths = LibraryScanner.ReadRoots(builder.Configuration, dataDirectory);
    return configuredPaths.Count == 0 && identity.LibraryPath is { } libraryPath
        ? [LibraryScanner.NormalizeRoot(libraryPath, dataDirectory, configuredPaths)]
        : configuredPaths;
}));
builder.Services.AddSingleton<LibraryScanQueue>();
builder.Services.AddSingleton<BookReader>();
builder.Services.AddSingleton(services => new ManagedFileOperations(services.GetRequiredService<LibraryStore>(), dataDirectory));
builder.Services.AddSingleton(_ => new MetadataAgent(builder.Configuration["MANGA_ORGANIZER_METADATA_SERVICE_URL"]));
builder.Services.AddHostedService(services => services.GetRequiredService<LibraryScanQueue>());
builder.Services.AddHostedService<MetadataSuggestionWorker>();

builder.Services.AddDataProtection()
    .SetApplicationName($"MangaOrganizer/{identity.InstanceId:D}")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDirectory, "keys")));
builder.Services.AddAuthentication(sessionScheme).AddCookie(options =>
{
    options.Cookie.Name = $"MangaOrganizer.Session.{identity.InstanceId:N}";
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.ExpireTimeSpan = TimeSpan.FromHours(12);
    options.SlidingExpiration = false;
    options.Events.OnRedirectToLogin = context =>
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    };
});
builder.Services.AddAuthorization();
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-Token";
    options.Cookie.Name = $"MangaOrganizer.Csrf.{identity.InstanceId:N}";
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("sign-in", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 8, Window = TimeSpan.FromMinutes(1) }));
});

var app = builder.Build();
app.UseWebUi();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));
var api = app.MapGroup("/api");
api.MapGet("/csrf", (HttpContext context, IAntiforgery antiforgery) =>
    Results.Ok(new { token = antiforgery.GetAndStoreTokens(context).RequestToken }));

api.MapPost("/session", async (SignIn request, HttpContext context, IAntiforgery antiforgery) =>
{
    if (!await antiforgery.IsRequestValidAsync(context))
    {
        return Results.BadRequest();
    }
    var ownerId = identity.OwnerId.ToString("D");
    if (!ServerStore.IsValidPassword(request.Password) || new PasswordHasher<string>()
        .VerifyHashedPassword(ownerId, identity.PasswordHash, request.Password) == PasswordVerificationResult.Failed)
    {
        return Results.Unauthorized();
    }

    var owner = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, ownerId)], sessionScheme);
    await context.SignInAsync(sessionScheme, new ClaimsPrincipal(owner));
    return Results.NoContent();
}).RequireRateLimiting("sign-in").WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(4096));

api.MapGet("/server", () => Results.Ok(new { id = identity.InstanceId })).RequireAuthorization();
api.MapLibrary(dataDirectory);
api.MapReader();
api.MapFileOperations();
api.MapMetadataQueue();
api.MapMaintenance();
api.MapVersions();
api.MapFileAssociations();

api.MapDelete("/session", async (HttpContext context, IAntiforgery antiforgery) =>
{
    if (!await antiforgery.IsRequestValidAsync(context))
    {
        return Results.BadRequest();
    }
    await context.SignOutAsync(sessionScheme);
    return Results.NoContent();
}).RequireAuthorization();

await app.RunAsync();

static FileStream? AcquireDataLease(string directory)
{
    try
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, ".server.lock");
        UpgradeBackup.RequireOrdinaryPath(path);
        return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
    {
        Console.Error.WriteLine("Could not lock the server data directory. Check its permissions and stop any other Manga Organizer process using it.");
        Environment.ExitCode = 1;
        return null;
    }
}

internal sealed record SignIn(string? Password);
