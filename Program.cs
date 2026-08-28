using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using oyinQ.Bot.Common.Normalization;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Admin;
using oyinQ.Bot.Features.Collections;
using oyinQ.Bot.Features.Communities;
using oyinQ.Bot.Features.Games;
using oyinQ.Bot.Features.Gatherings;
using oyinQ.Bot.Features.Interests;
using oyinQ.Bot.Features.MiniApp;
using oyinQ.Bot.Features.Registration;
using oyinQ.Bot.Features.Sessions;
using oyinQ.Bot.Integrations.BoardGameGeek;
using oyinQ.Bot.Integrations.Telegram;
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
var communityOptions = CommunityOptions.FromConfiguration(builder.Configuration);

builder.Services.AddSingleton(Options.Create(botOptions));
builder.Services.AddSingleton(Options.Create(campOptions));
builder.Services.AddSingleton(Options.Create(bggOptions));
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddDbContext<AppDbContext>(
    options => options.UseNpgsql(connectionString));

builder.Services.AddHttpClient();
builder.Services.AddHttpClient<IBoardGameGeekClient, BoardGameGeekClient>(client =>
{
    client.BaseAddress = new Uri("https://boardgamegeek.com");
    client.Timeout = TimeSpan.FromSeconds(60);
});
builder.Services.AddSingleton<ITelegramBotClient>(
    _ => new TelegramBotClient(botOptions.Token));

builder.Services.AddSingleton<GameNameNormalizer>();
builder.Services.AddSingleton<SessionMessageFormatter>();
builder.Services.AddSingleton<GatheringPresentationService>();
builder.Services.AddSingleton<ICommunityMembershipVerifier, TelegramCommunityMembershipVerifier>();
builder.Services.AddSingleton<ICampChatValidator, TelegramCampChatValidator>();
builder.Services.AddScoped<ICommunityStore, CommunityStore>();
builder.Services.AddScoped<CommunityContextResolver>();
builder.Services.AddScoped<CampCreationService>();
builder.Services.AddScoped<ClubCollectionService>();
builder.Services.AddScoped<ClubBggSyncService>();
builder.Services.AddScoped<CampContributionSelectionService>();
builder.Services.AddScoped<CampBggImportService>();
builder.Services.AddScoped<GatheringGameSelectionService>();
builder.Services.AddScoped<GatheringService>();
builder.Services.AddSingleton<TelegramMiniAppAuthenticator>();
builder.Services.AddSingleton<GatheringTelegramPublisher>();
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

app.UseDefaultFiles();
app.UseStaticFiles();

await using (var scope = app.Services.CreateAsyncScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await dbContext.Database.MigrateAsync();

    var existingCommunities = await dbContext.OyinQCommunities
        .Select(value => new { value.Key, value.TelegramChatId })
        .ToArrayAsync();
    var existingCommunityKeys = existingCommunities
        .Select(value => value.Key)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
    var existingCommunityChatIds = existingCommunities
        .Select(value => value.TelegramChatId)
        .ToHashSet();
    var now = DateTimeOffset.UtcNow;
    foreach (var community in communityOptions.Communities.Where(value =>
                 !existingCommunityKeys.Contains(value.Key)
                 && !existingCommunityChatIds.Contains(value.TelegramChatId)))
    {
        var botChat = new OyinQCommunity
        {
            Key = community.Key,
            Name = community.Name,
            TelegramChatId = community.TelegramChatId,
            Mode = community.Mode,
            TimeZoneId = community.TimeZoneId,
            CreatedAt = now,
            UpdatedAt = now
        };
        dbContext.OyinQCommunities.Add(botChat);
        if (community.Mode == BotMode.Club)
        {
            dbContext.Clubs.Add(new Club
            {
                BotChat = botChat,
                BotChatKey = community.Key,
                Name = community.Name,
                CollectionJson = ClubCollectionSerializer.Serialize(ClubCollectionDocument.Empty),
                CreatedAt = now,
                UpdatedAt = now
            });
        }
        else
        {
            dbContext.Camps.Add(new Camp
            {
                BotChat = botChat,
                BotChatKey = community.Key,
                Name = community.Name,
                BaseCollectionJson = ClubCollectionSerializer.Serialize(ClubCollectionDocument.Empty),
                Status = CampStatus.Active,
                CreatedByTelegramUserId = 0,
                CreatedAt = now,
                UpdatedAt = now
            });
        }
    }

    await dbContext.SaveChangesAsync();

    var missingClubs = await dbContext.OyinQCommunities
        .Where(value => value.Mode == BotMode.Club && value.Club == null)
        .ToArrayAsync();
    dbContext.Clubs.AddRange(missingClubs.Select(value => new Club
    {
        BotChatKey = value.Key,
        Name = value.Name,
        CollectionJson = ClubCollectionSerializer.Serialize(ClubCollectionDocument.Empty),
        CreatedAt = now,
        UpdatedAt = now
    }));
    var missingCamps = await dbContext.OyinQCommunities
        .Where(value => value.Mode == BotMode.Camp && value.Camp == null)
        .ToArrayAsync();
    dbContext.Camps.AddRange(missingCamps.Select(value => new Camp
    {
        BotChatKey = value.Key,
        Name = value.Name,
        BaseCollectionJson = ClubCollectionSerializer.Serialize(ClubCollectionDocument.Empty),
        Status = CampStatus.Active,
        CreatedByTelegramUserId = 0,
        CreatedAt = now,
        UpdatedAt = now
    }));
    await dbContext.SaveChangesAsync();
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

app.MapMiniAppEndpoints();
app.MapFallbackToFile("/app/{*path:nonfile}", "app/index.html");

app.Run();
