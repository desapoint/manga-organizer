using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;

namespace MangaOrganizer.Server;

internal static class WebSetup
{
    public static bool HasLoopbackUrls(IConfiguration configuration)
    {
        if (configuration.GetSection("Kestrel:Endpoints").Exists())
        {
            return false;
        }

        var urls = (configuration["urls"] ?? WebUi.DefaultUrl).Split(';', StringSplitOptions.TrimEntries);
        return urls.All(url => Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
            uri.Scheme is "http" or "https" && (uri.Host == "localhost" ||
            (IPAddress.TryParse(uri.IdnHost, out var address) && IPAddress.IsLoopback(address))));
    }

    public static async Task<ServerIdentity?> RunAsync(WebApplicationBuilder builder, string dataDirectory, IReadOnlyList<string> libraryRoots)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var tokenBytes = Encoding.ASCII.GetBytes(token);
        var setupLock = new object();
        ServerIdentity? completedIdentity = null;

        await using var app = builder.Build();
        app.UseWebUi();
        app.MapGet("/api/server", () => Results.Unauthorized());
        app.MapGet("/api/setup", (HttpContext context) => IsLocal(context)
            ? Results.Ok(new { libraryConfigured = libraryRoots.Count > 0 })
            : Results.NotFound());
        app.MapGet("/api/setup/folders", (HttpContext context, string? path) =>
            IsLocal(context) && MatchesToken(context.Request.Headers["X-Setup-Token"].ToString(), tokenBytes)
                ? FolderBrowser.Browse(path, dataDirectory)
                : Results.StatusCode(StatusCodes.Status403Forbidden));

        app.MapPost("/api/setup", (HttpContext context, SetupRequest request) =>
        {
            if (!IsLocal(context) || !MatchesToken(context.Request.Headers["X-Setup-Token"].ToString(), tokenBytes))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            if (!ServerStore.IsValidPassword(request.Password) || request.Password != request.ConfirmPassword)
            {
                return Results.BadRequest(new { message = "Use a matching password of 12–256 characters." });
            }

            lock (setupLock)
            {
                if (completedIdentity is not null)
                {
                    return Results.Conflict();
                }

                string? libraryPath = null;
                if (!string.IsNullOrWhiteSpace(request.LibraryPath))
                {
                    if (libraryRoots.Count > 0)
                    {
                        return Results.BadRequest(new { message = "Library folders are already configured on the server." });
                    }

                    try
                    {
                        libraryPath = LibraryScanner.NormalizeRoot(request.LibraryPath, dataDirectory, libraryRoots);
                        LibraryScanner.RequireRoot(libraryPath);
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

                var (identity, created) = ServerStore.Open(dataDirectory, request.Password, libraryPath);
                completedIdentity = identity;
                context.Response.OnCompleted(() =>
                {
                    app.Lifetime.StopApplication();
                    return Task.CompletedTask;
                });
                return created ? Results.NoContent() : Results.Conflict();
            }
        }).WithMetadata(new RequestSizeLimitAttribute(32 * 1024));

        await app.StartAsync();
        var setupUrl = $"{app.Urls.First().TrimEnd('/')}/#setup={token}";
        Console.WriteLine($"Open this one-time link to set up Manga Organizer: {setupUrl}");
        OpenBrowserOnWindows(setupUrl);
        await app.WaitForShutdownAsync();
        return completedIdentity;
    }

    private static bool IsLocal(HttpContext context) => context.Connection.RemoteIpAddress is { } address &&
        IPAddress.IsLoopback(address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address);

    private static bool MatchesToken(string supplied, byte[] expected) => supplied.Length == expected.Length &&
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(supplied), expected);

    private static void OpenBrowserOnWindows(string url)
    {
        if (!OperatingSystem.IsWindows() || !Environment.UserInteractive || Console.IsOutputRedirected)
        {
            return;
        }

        try
        {
            using var browser = Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch (Win32Exception)
        {
            Console.Error.WriteLine("Open the setup link above in your browser.");
        }
    }

    private sealed record SetupRequest(string? Password, string? ConfirmPassword, string? LibraryPath);
}
