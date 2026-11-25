namespace MangaOrganizer.Server;

internal static class WebUi
{
    public const string DefaultUrl = "http://127.0.0.1:8787";

    public static WebApplicationBuilder CreateBuilder(string[] args)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot"),
        });
        builder.Configuration["AllowedHosts"] ??= "localhost;127.0.0.1;[::1]";

        if (string.IsNullOrWhiteSpace(builder.Configuration["urls"]) &&
            !builder.Configuration.GetSection("Kestrel:Endpoints").Exists())
        {
            builder.WebHost.UseUrls(DefaultUrl);
        }

        return builder;
    }

    public static void UseWebUi(this WebApplication app)
    {
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
    }
}
