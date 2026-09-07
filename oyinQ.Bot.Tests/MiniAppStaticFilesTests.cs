using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using oyinQ.Bot.Features.MiniApp;

namespace oyinQ.Bot.Tests;

public sealed class MiniAppStaticFilesTests
{
    [Theory]
    [InlineData("/app/")]
    [InlineData("/app/index.html")]
    [InlineData("/app/?community=club&gathering=old-link")]
    [InlineData("/app/?admin=1")]
    [InlineData("/app/history/previous")]
    public async Task EntryDocuments_RevalidateIncludingConditionalResponses(string path)
    {
        var root = Path.Combine(Path.GetTempPath(), "oyinq-cache-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "app", "assets"));
        await File.WriteAllTextAsync(Path.Combine(root, "app", "index.html"), "<!doctype html><title>Current app</title>");
        await File.WriteAllTextAsync(Path.Combine(root, "app", "assets", "index-abcdef.js"), "export default 1;");
        try
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { WebRootPath = root });
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            await using var app = builder.Build();
            app.UseDefaultFiles();
            app.UseStaticFiles(MiniAppStaticFiles.Options());
            app.MapFallbackToFile("/app/{*path:nonfile}", "app/index.html", MiniAppStaticFiles.Options());
            await app.StartAsync();
            using var client = new HttpClient
            {
                BaseAddress = new Uri(app.Services.GetRequiredService<IServer>()
                    .Features.Get<IServerAddressesFeature>()!.Addresses.Single())
            };
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoCache);
            Assert.Contains("Current app", await response.Content.ReadAsStringAsync());

            using var conditional = new HttpRequestMessage(HttpMethod.Get, path);
            conditional.Headers.IfNoneMatch.Add(response.Headers.ETag!);
            using var unchanged = await client.SendAsync(conditional);
            Assert.Equal(HttpStatusCode.NotModified, unchanged.StatusCode);
            Assert.True(unchanged.Headers.CacheControl?.NoCache);

            using var bundle = await client.GetAsync("/app/assets/index-abcdef.js");
            Assert.Equal(HttpStatusCode.OK, bundle.StatusCode);
            Assert.Null(bundle.Headers.CacheControl);
            await app.StopAsync();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
