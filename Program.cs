using oyinQ.Bot.Features.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.Json.Serialization;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Admin;
using oyinQ.Bot.Features.Collections;
using oyinQ.Bot.Features.Catalog;
using oyinQ.Bot.Features.Communities;
using oyinQ.Bot.Features.Gatherings;
using oyinQ.Bot.Features.MiniApp;
using oyinQ.Bot.Features.PublicSite;
using oyinQ.Bot.Integrations.BoardGameGeek;
using oyinQ.Bot.Integrations.Telegram;
using Telegram.Bot;

var builder = WebApplication.CreateBuilder(args);

if (args.Contains("--reconcile-rollmove"))
{
    Environment.ExitCode = await RollMoveReconciliation.RunAsync(builder.Configuration);
    return;
}

if (args.Any(x => string.Equals(x, "--refresh-club-collection", StringComparison.OrdinalIgnoreCase)))
{
    Environment.ExitCode = await ClubCollectionRefreshGenerator.RunAsync(builder.Configuration, args);
    return;
}

if (args.Any(x => string.Equals(x, "--refresh-bgg-names", StringComparison.OrdinalIgnoreCase)))
{
    Environment.ExitCode = await BggNameRefreshCommand.RunAsync(builder.Configuration);
    return;
}

var connectionString = builder.Configuration["Database:ConnectionString"]?.Trim();
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("Database:ConnectionString is required.");
}

var botOptions = BotOptions.FromConfiguration(builder.Configuration);
var administrationOptions = AdministrationOptions.FromConfiguration(builder.Configuration);
var bggOptions = BggOptions.FromConfiguration(builder.Configuration);
var communityOptions = CommunityOptions.FromConfiguration(builder.Configuration);

builder.Services.AddSingleton(Options.Create(botOptions));
builder.Services.AddSingleton(Options.Create(administrationOptions));
builder.Services.AddSingleton(Options.Create(bggOptions));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSingleton<TelegramWebhookUpdateParser>();

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

builder.Services.AddSingleton<GatheringPresentationService>();
builder.Services.AddSingleton<MiniAppLinkBuilder>();
builder.Services.AddScoped<ICommunityMembershipVerifier, TelegramCommunityMembershipVerifier>();
builder.Services.AddSingleton<IManagedChatValidator, TelegramManagedChatValidator>();
builder.Services.AddScoped<ITelegramChatAdministratorVerifier, TelegramChatAdministratorVerifier>();
builder.Services.AddSingleton<ITelegramChatForumCapabilityResolver, TelegramChatForumCapabilityResolver>();
builder.Services.AddScoped<ICommunityStore, CommunityStore>();
builder.Services.AddScoped<IAdminAuthorizationService, AdminAuthorizationService>();
builder.Services.AddScoped<CampParticipantAdminService>();
builder.Services.AddScoped<CommunityDeletionService>();
builder.Services.AddScoped<TelegramCommunityPhotoService>();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<PostingTopicService>();
builder.Services.AddScoped<CommunityContextResolver>();
builder.Services.AddScoped<ManagedCommunityService>();
builder.Services.AddScoped<CampParticipationPolicy>();
builder.Services.AddScoped<CampRegistrationService>();
builder.Services.AddScoped<ClubCollectionService>();
builder.Services.AddScoped<ClubMetadataRefreshService>();
builder.Services.AddScoped<ClubBggImportService>();
builder.Services.AddScoped<CampContributionSelectionService>();
builder.Services.AddScoped<ParticipantCollectionService>();
builder.Services.AddScoped<GameCatalogService>();
builder.Services.AddScoped<GameProviderService>();
builder.Services.AddScoped<ReleaseAnnouncementService>();
builder.Services.AddHostedService<ReleaseAnnouncementWorker>();
builder.Services.AddScoped<GatheringPlayService>();
builder.Services.AddScoped<ExternalPlayReferenceService>();
builder.Services.AddScoped<GatheringDashboardService>();
builder.Services.AddScoped<EffectiveCampCatalogService>();
builder.Services.AddScoped<CampBggImportService>();
builder.Services.AddScoped<CampBggImportCoordinator>();
builder.Services.AddScoped<CampImportNotificationService>();
builder.Services.AddScoped<TelegramPeerSelectionService>();
builder.Services.AddSingleton<ITelegramCommunityOnboardingService, TelegramCommunityOnboardingService>();
builder.Services.AddScoped<GatheringGameSelectionService>();
builder.Services.AddScoped<GatheringService>();
builder.Services.Configure<GatheringPlanningOptions>(builder.Configuration.GetSection("Gatherings"));
builder.Services.AddScoped<GatheringScheduleConflictService>();
builder.Services.AddScoped<GatheringManagementService>();
builder.Services.AddScoped<GatheringPublicationService>();
builder.Services.AddScoped<GatheringNotificationService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<ProviderAttentionService>();
builder.Services.AddScoped<NotificationDispatcher>();
builder.Services.AddScoped<GatheringReminderService>();
builder.Services.AddScoped<INotificationTransport, TelegramNotificationTransport>();
builder.Services.AddHostedService<NotificationWorker>();
builder.Services.AddScoped<TelegramMessageCleanupProcessor>();
builder.Services.AddSingleton<ITelegramMessageDeletionClient, TelegramMessageDeletionClient>();
builder.Services.AddSingleton<TelegramMessageDeletionHandler>();
builder.Services.AddSingleton<TelegramMiniAppAuthenticator>();
builder.Services.AddScoped<ITelegramGroupMessageSender, TelegramGroupMessageSender>();
builder.Services.AddScoped<GatheringTelegramPublisher>();
builder.Services.AddScoped<CsvExportService>();
builder.Services.AddScoped<AdminHandler>();
builder.Services.AddScoped<TelegramUpdateHandler>();
builder.Services.AddScoped<ParticipantIdentityService>();
builder.Services.AddScoped<PrivateChatCapability>();
builder.Services.AddHostedService<CampBggImportWorker>();
builder.Services.AddHostedService<CampLifecycleWorker>();
builder.Services.AddHostedService<ClubMetadataRefreshWorker>();
builder.Services.AddHostedService<ClubBggImportWorker>();
builder.Services.AddHostedService<GatheringLifecycleWorker>();
builder.Services.AddHostedService<TelegramMessageCleanupWorker>();
builder.Services.AddHostedService<TelegramBotProfileSetupService>();

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
    var pendingMigrations = (await dbContext.Database.GetPendingMigrationsAsync()).ToArray();
    app.Logger.LogInformation("Обновление базы перед запуском: {Count} миграций. {Migrations}",
        pendingMigrations.Length, string.Join(", ", pendingMigrations));
    await dbContext.Database.MigrateAsync();
    app.Logger.LogInformation("Миграции базы завершены. Продолжается подготовка приложения.");

    var existingCommunities = await dbContext.OyinQCommunities
        .Select(value => new { value.Key, value.TelegramChatId })
        .ToArrayAsync();
    var existingCommunityKeys = existingCommunities
        .Select(value => value.Key)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
    var existingCommunityChatIds = await dbContext.OyinQCommunities
        .Where(value => value.DeletedAt == null)
        .Select(value => value.TelegramChatId)
        .ToHashSetAsync();
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
            IsActive = community.Mode == BotMode.Club,
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
                Status = CampStatus.Draft,
                CreatedByTelegramUserId = 0,
                CreatedAt = now,
                UpdatedAt = now
            });
        }
    }

    await dbContext.SaveChangesAsync();

    var knownChatIds = await dbContext.KnownTelegramChats
        .Select(value => value.TelegramChatId).ToHashSetAsync();
    var configuredChats = await dbContext.OyinQCommunities.AsNoTracking()
        .Where(value => value.DeletedAt == null && !knownChatIds.Contains(value.TelegramChatId))
        .Select(value => new { value.TelegramChatId, value.Name }).ToArrayAsync();
    dbContext.KnownTelegramChats.AddRange(configuredChats.Select(value => new KnownTelegramChat
    {
        TelegramChatId = value.TelegramChatId,
        Title = value.Name,
        IsBotPresent = true,
        FirstSeenAt = now,
        UpdatedAt = now
    }));
    await dbContext.SaveChangesAsync();

    var missingClubs = await dbContext.OyinQCommunities
        .Where(value => value.DeletedAt == null && value.Mode == BotMode.Club && value.Club == null)
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
        .Where(value => value.DeletedAt == null && value.Mode == BotMode.Camp && value.Camp == null)
        .ToArrayAsync();
    foreach (var community in missingCamps) community.IsActive = false;
    dbContext.Camps.AddRange(missingCamps.Select(value => new Camp
    {
        BotChatKey = value.Key,
        Name = value.Name,
        BaseCollectionJson = ClubCollectionSerializer.Serialize(ClubCollectionDocument.Empty),
        Status = CampStatus.Draft,
        CreatedByTelegramUserId = 0,
        CreatedAt = now,
        UpdatedAt = now
    }));
    await dbContext.SaveChangesAsync();
}

app.MapGet("/health", static () => Results.Ok());
app.MapGet("/ready", async (AppDbContext dbContext, CancellationToken cancellationToken) =>
{
    try
    {
        await dbContext.Database.ExecuteSqlRawAsync("SELECT 1", cancellationToken);
        return Results.Ok();
    }
    catch (Exception) when (!cancellationToken.IsCancellationRequested)
    {
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }
});
app.MapGet(PrivacyPolicyPage.Path, PrivacyPolicyPage.HandleAsync);

app.MapPost(
    "/telegram/webhook/{secret}",
    async Task<IResult> (
        string secret,
        HttpRequest request,
        IOptions<BotOptions> options,
        TelegramWebhookUpdateParser parser,
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

        if (!request.Headers.TryGetValue("X-Telegram-Bot-Api-Secret-Token", out var secretToken) ||
            !string.Equals(secretToken.ToString(), options.Value.WebhookSecret, StringComparison.Ordinal))
        {
            return Results.NotFound();
        }

        var update = await parser.ParseAsync(request.Body, cancellationToken);
        if (update is null)
        {
            return Results.BadRequest();
        }

        await handler.HandleAsync(update, cancellationToken);
        return Results.Ok();
    });

app.MapMiniAppEndpoints();
app.MapFallbackToFile("/app/{*path:nonfile}", "app/index.html");

app.Run();
