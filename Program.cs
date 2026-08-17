using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using oyinQ.Bot.Common.Normalization;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data;
using oyinQ.Bot.Features.Admin;
using oyinQ.Bot.Features.Collections;
using oyinQ.Bot.Features.Games;
using oyinQ.Bot.Features.Interests;
using oyinQ.Bot.Features.Registration;
using oyinQ.Bot.Features.Sessions;
using oyinQ.Bot.Integrations.BoardGameGeek;
using oyinQ.Bot.Integrations.Telegram;
using oyinQ.Bot.Integrations.Tesera;
using Telegram.Bot;
using Telegram.Bot.Types;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration["CONNECTION_STRING"]?.Trim();
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("CONNECTION_STRING is required.");
}

var botOptions = BotOptions.FromConfiguration(builder.Configuration);
var campOptions = CampOptions.FromConfiguration(builder.Configuration);
var bggOptions = BggOptions.FromConfiguration(builder.Configuration);

builder.Services.AddSingleton(Options.Create(botOptions));
builder.Services.AddSingleton(Options.Create(campOptions));
builder.Services.AddSingleton(Options.Create(bggOptions));

builder.Services.AddDbContext<AppDbContext>(
    options => options.UseNpgsql(connectionString));

builder.Services.AddHttpClient();
builder.Services.AddHttpClient<IBoardGameGeekClient, BoardGameGeekClient>(client =>
{
    client.BaseAddress = new Uri("https://boardgamegeek.com");
    client.Timeout = TimeSpan.FromSeconds(60);
});
builder.Services.AddHttpClient<ITeseraClient, TeseraClient>(client =>
{
    client.BaseAddress = new Uri("https://api.tesera.ru");
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddSingleton<ITelegramBotClient>(
    _ => new TelegramBotClient(botOptions.Token));

builder.Services.AddSingleton<GameNameNormalizer>();
builder.Services.AddSingleton<SessionMessageFormatter>();
builder.Services.AddScoped<RegistrationHandler>();
builder.Services.AddScoped<GameDedupService>();
builder.Services.AddScoped<GameSearchService>();
builder.Services.AddScoped<GamesHandler>();
builder.Services.AddScoped<InterestsHandler>();
builder.Services.AddScoped<SessionsHandler>();
builder.Services.AddScoped<CollectionImportService>();
builder.Services.AddScoped<CollectionsHandler>();
builder.Services.AddScoped<CsvExportService>();
builder.Services.AddScoped<AdminHandler>();
builder.Services.AddScoped<TelegramUpdateHandler>();
builder.Services.AddHostedService<CollectionImportWorker>();

if (botOptions.UseLongPolling)
{
    builder.Services.AddHostedService<TelegramPollingService>();
}
else
{
    builder.Services.AddHostedService<TelegramWebhookSetupService>();
}

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await dbContext.Database.MigrateAsync();
}

app.MapGet("/health", static () => Results.Ok());

app.MapPost(
    "/telegram/webhook/{secret}",
    async Task<IResult> (
        string secret,
        Update update,
        IOptions<BotOptions> options,
        TelegramUpdateHandler handler,
        CancellationToken cancellationToken) =>
    {
        if (!string.Equals(
                secret,
                options.Value.WebhookSecret,
                StringComparison.Ordinal))
        {
            return Results.NotFound();
        }

        await handler.HandleAsync(update, cancellationToken);
        return Results.Ok();
    });

app.Run();
