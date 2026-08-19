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

var teseraBaseUrl = builder.Configuration["TESERA_BASE_URL"]?.Trim();
if (string.IsNullOrWhiteSpace(teseraBaseUrl))
{
    teseraBaseUrl = "https://api.tesera.ru";
}

if (!Uri.TryCreate(teseraBaseUrl, UriKind.Absolute, out var teseraBaseUri)
    || teseraBaseUri.Scheme != Uri.UriSchemeHttps)
{
    throw new InvalidOperationException("TESERA_BASE_URL must be an absolute HTTPS URL.");
}

var teseraProxyToken = builder.Configuration["TESERA_PROXY_TOKEN"]?.Trim();
var botOptions = BotOptions.FromConfiguration(builder.Configuration);
var campOptions = CampOptions.FromConfiguration(builder.Configuration);
var bggOptions = BggOptions.FromConfiguration(builder.Configuration);

builder.Services.AddSingleton(Options.Create(botOptions));
builder.Services.AddSingleton(Options.Create(campOptions));
builder.Services.AddSingleton(Options.Create(bggOptions));

builder.Services.AddDbContext<AppDbContext>(
    options => options.UseNpgsql(connectionString));

builder.Services.AddMemoryCache();
builder.Services.AddHttpClient();
builder.Services.AddHttpClient<IBoardGameGeekClient, BoardGameGeekClient>(client =>
{
    client.BaseAddress = new Uri("https://boardgamegeek.com");
    client.Timeout = TimeSpan.FromSeconds(60);
});
builder.Services.AddHttpClient<ITeseraClient, TeseraClient>(client =>
{
    client.BaseAddress = teseraBaseUri;
    client.Timeout = TimeSpan.FromSeconds(30);
    if (!string.IsNullOrWhiteSpace(teseraProxyToken))
    {
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", teseraProxyToken);
    }
});
builder.Services.AddSingleton<TeseraAvailabilityService>();
builder.Services.AddSingleton<ITelegramBotClient>(
    _ => new TelegramBotClient(botOptions.Token));

builder.Services.AddSingleton<GameNameNormalizer>();
builder.Services.AddSingleton<SessionMessageFormatter>();
builder.Services.AddScoped<RegistrationHandler>();
builder.Services.AddScoped<GameDedupService>();
builder.Services.AddScoped<GameSearchService>();
builder.Services.AddScoped<GamesHandler>();
builder.Services.AddScoped<GamesUxPresenter>();
builder.Services.AddScoped<InterestsHandler>();
builder.Services.AddScoped<SessionsHandler>();
builder.Services.AddScoped<CollectionImportService>();
builder.Services.AddScoped<CollectionsHandler>();
builder.Services.AddScoped<CsvExportService>();
builder.Services.AddScoped<AdminHandler>();
builder.Services.AddScoped<TelegramUpdateHandler>();
builder.Services.AddHostedService<TeseraAvailabilityMonitorService>();
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

app.MapGet(
    "/health/tesera",
    async Task<IResult> (
        TeseraAvailabilityService availabilityService,
        CancellationToken cancellationToken) =>
    {
        var availability = await availabilityService.GetAsync(
            forceRefresh: false,
            cancellationToken);

        var body = new
        {
            dependency = "tesera",
            status = availability.IsAvailable ? "ok" : "unavailable",
            reason = availability.Reason,
            checkedAt = availability.CheckedAt
        };

        return availability.IsAvailable
            ? Results.Ok(body)
            : Results.Json(body, statusCode: StatusCodes.Status503ServiceUnavailable);
    });

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
