using System.Security.Claims;
using System.Threading.RateLimiting;
using MangaOrganizer.Server;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;

const string sessionScheme = CookieAuthenticationDefaults.AuthenticationScheme;
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot"),
});
builder.Configuration["AllowedHosts"] ??= "localhost;127.0.0.1;[::1]";

if (string.IsNullOrWhiteSpace(builder.Configuration["urls"]) &&
    !builder.Configuration.GetSection("Kestrel:Endpoints").Exists())
{
    builder.WebHost.UseUrls("http://127.0.0.1:8787");
}

var dataDirectory = Path.GetFullPath(builder.Configuration["MANGA_ORGANIZER_DATA_DIR"] ?? Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify),
    "MangaOrganizer"));
Directory.CreateDirectory(dataDirectory);
var identity = ServerStore.Open(dataDirectory, builder.Configuration["MANGA_ORGANIZER_BOOTSTRAP_PASSWORD"]);

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
app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.ContentSecurityPolicy = "default-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";
    if (context.Request.Path.StartsWithSegments("/api") && context.Request.Path != "/api/csrf")
    {
        context.Response.Headers.CacheControl = "no-store";
    }
    await next(context);
});
app.UseDefaultFiles();
app.UseStaticFiles();
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
    if (request.Password is not { Length: >= 12 and <= 256 } || new PasswordHasher<string>()
        .VerifyHashedPassword(ownerId, identity.PasswordHash, request.Password) == PasswordVerificationResult.Failed)
    {
        return Results.Unauthorized();
    }

    var owner = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, ownerId)], sessionScheme);
    await context.SignInAsync(sessionScheme, new ClaimsPrincipal(owner));
    return Results.NoContent();
}).RequireRateLimiting("sign-in").WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(4096));

api.MapGet("/server", () => Results.Ok(new { id = identity.InstanceId })).RequireAuthorization();

api.MapDelete("/session", async (HttpContext context, IAntiforgery antiforgery) =>
{
    if (!await antiforgery.IsRequestValidAsync(context))
    {
        return Results.BadRequest();
    }
    await context.SignOutAsync(sessionScheme);
    return Results.NoContent();
}).RequireAuthorization();

app.Run();

internal sealed record SignIn(string? Password);
