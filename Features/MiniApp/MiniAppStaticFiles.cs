namespace oyinQ.Bot.Features.MiniApp;

public static class MiniAppStaticFiles
{
    public static StaticFileOptions Options() => new()
    {
        OnPrepareResponse = context =>
        {
            // Also runs for the SPA fallback, whose original URL need not end in .html.
            // Bundles already have content hashes; the entry document must discover new hashes.
            if (context.Context.Request.Path.StartsWithSegments("/app") &&
                string.Equals(context.File.Name, "index.html", StringComparison.OrdinalIgnoreCase))
            {
                context.Context.Response.Headers.CacheControl = "no-cache";
            }
        }
    };
}
