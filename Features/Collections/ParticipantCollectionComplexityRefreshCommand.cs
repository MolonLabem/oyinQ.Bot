using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data;
using oyinQ.Bot.Integrations.BoardGameGeek;

namespace oyinQ.Bot.Features.Collections;

public static class ParticipantCollectionComplexityRefreshCommand
{
    public static async Task<int> RunAsync(IConfiguration configuration, string[] args, CancellationToken ct = default)
    {
        var connectionString = configuration["Database:ConnectionString"]?.Trim();
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Database__ConnectionString is required.");
        var options = BggOptions.FromConfiguration(configuration);
        if (!options.IsAvailable)
            throw new InvalidOperationException("BoardGameGeek__ApiToken is required.");
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString).Options);
        using var http = new HttpClient { BaseAddress = new Uri("https://boardgamegeek.com"), Timeout = TimeSpan.FromSeconds(60) };
        var apply = args.Contains("--apply", StringComparer.OrdinalIgnoreCase);
        var result = await new ParticipantCollectionComplexityRefresh(db,
            new BoardGameGeekClient(http, Options.Create(options)), TimeProvider.System).RunAsync(apply, ct);
        Console.WriteLine(JsonSerializer.Serialize(new { Mode = apply ? "apply" : "dry-run", Result = result }));
        return 0;
    }
}
