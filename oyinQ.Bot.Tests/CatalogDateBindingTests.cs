using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace oyinQ.Bot.Tests;

public sealed class CatalogDateBindingTests
{
    [Fact]
    public async Task OptionalCatalogDateMustBeOmittedRatherThanSentEmpty()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var app = builder.Build();
        // Match the catalog endpoint's optional DateOnly query contract.
        app.MapGet("/catalog/{bggId:long}", (long bggId, DateOnly? attendanceDate) =>
            attendanceDate?.ToString("yyyy-MM-dd") ?? "all-days");
        await app.StartAsync();
        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient { BaseAddress = new Uri(address) };
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/catalog/315895?attendanceDate=")).StatusCode);
            Assert.Equal("all-days", await client.GetStringAsync("/catalog/315895"));
            Assert.Equal("2026-09-26", await client.GetStringAsync("/catalog/315895?attendanceDate=2026-09-26"));
        }
        finally { await app.StopAsync(); }
    }
}
