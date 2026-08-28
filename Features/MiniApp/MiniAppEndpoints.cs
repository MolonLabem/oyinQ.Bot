using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Globalization;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Collections;
using oyinQ.Bot.Features.Communities;
using oyinQ.Bot.Features.Gatherings;
using oyinQ.Bot.Integrations.BoardGameGeek;
using oyinQ.Bot.Integrations.Telegram;

namespace oyinQ.Bot.Features.MiniApp;

public static class MiniAppEndpoints
{
    public static IEndpointRouteBuilder MapMiniAppEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/miniapp");
        group.MapGet("/communities", GetCommunitiesAsync);
        group.MapGet("/admin/communities", GetAdminCommunitiesAsync);
        group.MapPost("/admin/communities", CreateCommunityAsync);
        group.MapGet("/admin/clubs", GetAdminClubsAsync);
        group.MapGet("/admin/clubs/{clubId:long}/collection", GetClubCollectionAsync);
        group.MapPut("/admin/clubs/{clubId:long}/collection", ReplaceClubCollectionAsync);
        group.MapPost("/admin/clubs/{clubId:long}/games", AddClubGameAsync);
        group.MapDelete("/admin/clubs/{clubId:long}/games/{bggId:long}", RemoveClubGameAsync);
        group.MapGet("/camp/registration", GetCampRegistrationAsync);
        group.MapPut("/camp/registration", SaveCampRegistrationAsync);
        group.MapPost("/camp/import/preview", PreviewCampImportAsync);
        group.MapPut("/camp/contributions", SaveCampContributionsAsync);
        group.MapPost("/camp/catalog/games", AddManualCampGameAsync);
        group.MapGet("/games", GetGamesAsync);
        group.MapGet("/bgg/game", GetBggGameAsync);
        group.MapGet("/gatherings", GetGatheringsAsync);
        group.MapGet("/gatherings/{publicId:guid}", GetGatheringAsync);
        group.MapPost("/gatherings", CreateGatheringAsync);
        group.MapPost("/gatherings/{publicId:guid}/join", JoinGatheringAsync);
        group.MapPost("/gatherings/{publicId:guid}/leave", LeaveGatheringAsync);
        group.MapPut("/gatherings/{publicId:guid}/presentation", UpdatePresentationAsync);
        return endpoints;
    }

    private static async Task<IResult> GetAdminClubsAsync(
        HttpRequest request,
        AppDbContext dbContext,
        TelegramMiniAppAuthenticator authenticator,
        IOptions<CampOptions> campOptions,
        CancellationToken cancellationToken)
    {
        var access = AuthenticateAdmin(request, authenticator, campOptions);
        if (access.Result is not null) return access.Result;
        var clubs = await dbContext.Clubs.AsNoTracking().OrderBy(value => value.Name).ToArrayAsync(cancellationToken);
        return Results.Ok(clubs.Select(value => new
        {
            value.Id,
            value.BotChatKey,
            value.Name,
            GameCount = value.ReadCollection().Games.Count
        }));
    }

    private static async Task<IResult> GetClubCollectionAsync(
        HttpRequest request,
        long clubId,
        ClubCollectionService collectionService,
        TelegramMiniAppAuthenticator authenticator,
        IOptions<CampOptions> campOptions,
        CancellationToken cancellationToken)
    {
        var access = AuthenticateAdmin(request, authenticator, campOptions);
        if (access.Result is not null) return access.Result;
        try { return Results.Ok(await collectionService.GetAsync(clubId, cancellationToken)); }
        catch (KeyNotFoundException) { return Results.NotFound(); }
    }

    private static async Task<IResult> ReplaceClubCollectionAsync(
        HttpRequest request,
        long clubId,
        ClubCollectionDocument document,
        ClubCollectionService collectionService,
        TelegramMiniAppAuthenticator authenticator,
        IOptions<CampOptions> campOptions,
        CancellationToken cancellationToken)
    {
        var access = AuthenticateAdmin(request, authenticator, campOptions);
        if (access.Result is not null) return access.Result;
        try
        {
            await collectionService.ReplaceAsync(clubId, document, DateTimeOffset.UtcNow, cancellationToken);
            return Results.NoContent();
        }
        catch (KeyNotFoundException) { return Results.NotFound(); }
        catch (InvalidOperationException exception) { return Results.BadRequest(new { message = exception.Message }); }
    }

    private static async Task<IResult> AddClubGameAsync(
        HttpRequest request,
        long clubId,
        AddClubGameRequest body,
        ClubCollectionService collectionService,
        IBoardGameGeekClient bggClient,
        TelegramMiniAppAuthenticator authenticator,
        IOptions<CampOptions> campOptions,
        CancellationToken cancellationToken)
    {
        var access = AuthenticateAdmin(request, authenticator, campOptions);
        if (access.Result is not null) return access.Result;
        var details = await bggClient.GetGameDetailsAsync(body.BggId, cancellationToken);
        if (details is null) return Results.NotFound();
        var selected = body.ExpansionBggIds ?? [];
        if (selected.Any(value => details.Expansions.All(expansion => expansion.BggId != value)))
        {
            return Results.BadRequest(new { message = "Выбрано дополнение, которое BGG не связывает с этой игрой." });
        }
        var game = details.Game;
        try
        {
            await collectionService.AddOrReplaceGameAsync(
                clubId,
                new ClubCollectionGame(
                    body.BggId,
                    game.Name,
                    game.ThumbnailImageUrl,
                    game.ImageUrl,
                    game.MinPlayers,
                    game.MaxPlayers,
                    game.BestPlayers,
                    details.Expansions.Where(value => selected.Contains(value.BggId))
                        .Select(value => new ClubCollectionExpansion(value.BggId, value.Name))
                        .ToArray()),
                DateTimeOffset.UtcNow,
                cancellationToken);
            return Results.NoContent();
        }
        catch (KeyNotFoundException) { return Results.NotFound(); }
    }

    private static async Task<IResult> RemoveClubGameAsync(
        HttpRequest request,
        long clubId,
        long bggId,
        ClubCollectionService collectionService,
        TelegramMiniAppAuthenticator authenticator,
        IOptions<CampOptions> campOptions,
        CancellationToken cancellationToken)
    {
        var access = AuthenticateAdmin(request, authenticator, campOptions);
        if (access.Result is not null) return access.Result;
        try
        {
            return await collectionService.RemoveGameAsync(clubId, bggId, DateTimeOffset.UtcNow, cancellationToken)
                ? Results.NoContent()
                : Results.NotFound();
        }
        catch (KeyNotFoundException) { return Results.NotFound(); }
    }

    private static async Task<IResult> GetCampRegistrationAsync(
        HttpRequest request,
        string community,
        AppDbContext dbContext,
        TelegramMiniAppAuthenticator authenticator,
        CommunityContextResolver resolver,
        CancellationToken cancellationToken)
    {
        var access = await AuthorizeAsync(request, community, authenticator, resolver, cancellationToken);
        if (access.Result is not null) return access.Result;
        if (access.Community!.Mode != BotMode.Camp) return Results.BadRequest(new { message = "Регистрация нужна только для кэмпа." });
        var registration = await dbContext.CampRegistrations.AsNoTracking()
            .Where(value => value.Camp.BotChatKey == access.Community.Key
                && value.Participant.TelegramUserId == access.Identity!.TelegramUserId)
            .Select(value => new { Registered = true, value.DaysStaying, value.NeedsAccommodation })
            .SingleOrDefaultAsync(cancellationToken);
        return Results.Ok(registration ?? new { Registered = false, DaysStaying = (int?)null, NeedsAccommodation = (bool?)null });
    }

    private static async Task<IResult> SaveCampRegistrationAsync(
        HttpRequest request,
        CampRegistrationRequest body,
        AppDbContext dbContext,
        TelegramMiniAppAuthenticator authenticator,
        CommunityContextResolver resolver,
        CancellationToken cancellationToken)
    {
        var access = await AuthorizeAsync(request, body.CommunityKey, authenticator, resolver, cancellationToken);
        if (access.Result is not null) return access.Result;
        if (access.Community!.Mode != BotMode.Camp) return Results.BadRequest(new { message = "Регистрация нужна только для кэмпа." });
        if (body.DaysStaying is < 1 or > 30) return Results.BadRequest(new { message = "Укажите количество дней от 1 до 30." });

        var participant = await GetOrCreateParticipantAsync(dbContext, access, cancellationToken);
        var campId = await dbContext.Camps.Where(value => value.BotChatKey == access.Community.Key)
            .Select(value => value.Id)
            .SingleAsync(cancellationToken);
        var registration = await dbContext.CampRegistrations.SingleOrDefaultAsync(
            value => value.CampId == campId && value.ParticipantId == participant.Id,
            cancellationToken);
        var now = DateTimeOffset.UtcNow;
        if (registration is null)
        {
            registration = new CampRegistration
            {
                CampId = campId,
                ParticipantId = participant.Id,
                CreatedAt = now
            };
            dbContext.CampRegistrations.Add(registration);
        }
        registration.DaysStaying = body.DaysStaying;
        registration.NeedsAccommodation = body.NeedsAccommodation;
        registration.UpdatedAt = now;
        participant.PreferredDisplayName = string.IsNullOrWhiteSpace(body.DisplayName) ? participant.PreferredDisplayName : body.DisplayName.Trim();
        participant.ActiveCommunityKey = access.Community.Key;
        participant.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> PreviewCampImportAsync(
        HttpRequest request,
        CampImportPreviewRequest body,
        AppDbContext dbContext,
        TelegramMiniAppAuthenticator authenticator,
        CommunityContextResolver resolver,
        CampBggImportService importService,
        CancellationToken cancellationToken)
    {
        var access = await AuthorizeAsync(request, body.CommunityKey, authenticator, resolver, cancellationToken);
        if (access.Result is not null) return access.Result;
        var modeAccess = await EnsureModeAccessAsync(dbContext, access, cancellationToken);
        if (modeAccess is not null) return modeAccess;
        if (access.Community!.Mode != BotMode.Camp) return Results.BadRequest(new { message = "Личный импорт доступен только в кэмпе." });
        var username = BggUsernameParser.Parse(body.BggInput);
        if (username is null) return Results.BadRequest(new { message = "Не удалось распознать имя пользователя BGG." });
        try
        {
            var selection = await importService.LoadSelectionAsync(username, cancellationToken);
            return Results.Ok(new { Username = username, Items = selection, Groups = CampBggImportService.BuildGroups(selection) });
        }
        catch (HttpRequestException exception)
        {
            return Results.BadRequest(new { message = $"BGG временно недоступен: {exception.Message}" });
        }
    }

    private static async Task<IResult> SaveCampContributionsAsync(
        HttpRequest request,
        SaveCampContributionsRequest body,
        AppDbContext dbContext,
        TelegramMiniAppAuthenticator authenticator,
        CommunityContextResolver resolver,
        CampContributionSelectionService selectionService,
        CancellationToken cancellationToken)
    {
        var access = await AuthorizeAsync(request, body.CommunityKey, authenticator, resolver, cancellationToken);
        if (access.Result is not null) return access.Result;
        var modeAccess = await EnsureModeAccessAsync(dbContext, access, cancellationToken);
        if (modeAccess is not null) return modeAccess;
        if (access.Community!.Mode != BotMode.Camp) return Results.BadRequest(new { message = "Личный импорт доступен только в кэмпе." });
        var participantId = await dbContext.Participants.Where(value => value.TelegramUserId == access.Identity!.TelegramUserId)
            .Select(value => value.Id)
            .SingleAsync(cancellationToken);
        var campId = await dbContext.Camps.Where(value => value.BotChatKey == access.Community.Key)
            .Select(value => value.Id)
            .SingleAsync(cancellationToken);
        try
        {
            await selectionService.SaveSelectionAsync(campId, participantId, body.Items, DateTimeOffset.UtcNow, cancellationToken);
            return Results.NoContent();
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or InvalidOperationException)
        {
            return Results.BadRequest(new { message = exception.Message });
        }
    }

    private static async Task<IResult> AddManualCampGameAsync(
        HttpRequest request,
        CampManualGameRequest body,
        AppDbContext dbContext,
        TelegramMiniAppAuthenticator authenticator,
        CommunityContextResolver resolver,
        IBoardGameGeekClient bggClient,
        CancellationToken cancellationToken)
    {
        var access = await AuthorizeAsync(request, body.CommunityKey, authenticator, resolver, cancellationToken);
        if (access.Result is not null) return access.Result;
        var modeAccess = await EnsureModeAccessAsync(dbContext, access, cancellationToken);
        if (modeAccess is not null) return modeAccess;
        if (access.Community!.Mode != BotMode.Camp) return Results.BadRequest(new { message = "Ручное добавление доступно только в кэмпе." });
        var bggId = BggGameUrlParser.Parse(body.BggInput);
        if (bggId is null) return Results.BadRequest(new { message = "Вставьте корректную ссылку BGG." });
        var details = await bggClient.GetGameDetailsAsync(bggId.Value, cancellationToken);
        if (details is null) return Results.NotFound();
        var selectedExpansionIds = body.ExpansionBggIds ?? [];
        if (selectedExpansionIds.Any(value => details.Expansions.All(expansion => expansion.BggId != value)))
        {
            return Results.BadRequest(new { message = "Выбрано неизвестное дополнение." });
        }

        var participantId = await dbContext.Participants.Where(value => value.TelegramUserId == access.Identity!.TelegramUserId)
            .Select(value => value.Id)
            .SingleAsync(cancellationToken);
        var campId = await dbContext.Camps.Where(value => value.BotChatKey == access.Community.Key)
            .Select(value => value.Id)
            .SingleAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var requested = new List<CampImportSelectionItem>
        {
            new(
                bggId.Value,
                CampContributionItemType.BaseGame,
                null,
                details.Game.Name,
                true,
                details.Game.ThumbnailImageUrl,
                details.Game.ImageUrl,
                details.Game.MinPlayers,
                details.Game.MaxPlayers,
                details.Game.BestPlayers)
        };
        requested.AddRange(details.Expansions.Where(value => selectedExpansionIds.Contains(value.BggId)).Select(value =>
            new CampImportSelectionItem(value.BggId, CampContributionItemType.Expansion, bggId, value.Name, true)));
        foreach (var item in requested)
        {
            var contribution = await dbContext.CampGameContributions.SingleOrDefaultAsync(value =>
                value.CampId == campId && value.ParticipantId == participantId
                && value.BggId == item.BggId && value.ItemType == item.ItemType,
                cancellationToken);
            if (contribution is null)
            {
                contribution = new CampGameContribution
                {
                    CampId = campId,
                    ParticipantId = participantId,
                    BggId = item.BggId,
                    ItemType = item.ItemType,
                    CreatedAt = now
                };
                dbContext.CampGameContributions.Add(contribution);
            }
            contribution.ParentBggId = item.ParentBggId;
            contribution.SnapshotJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                item.Name,
                item.ThumbnailImageUrl,
                item.ImageUrl,
                item.MinPlayers,
                item.MaxPlayers,
                item.BestPlayers
            }, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
            contribution.UpdatedAt = now;
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> GetAdminCommunitiesAsync(
        HttpRequest request,
        AppDbContext dbContext,
        TelegramMiniAppAuthenticator authenticator,
        IOptions<CampOptions> campOptions,
        CancellationToken cancellationToken)
    {
        var identity = AuthenticateAdmin(request, authenticator, campOptions);
        if (identity.Result is not null) return identity.Result;

        var communities = await dbContext.OyinQCommunities.AsNoTracking()
            .OrderBy(value => value.Name)
            .Select(value => new
            {
                value.Key,
                value.Name,
                value.TelegramChatId,
                Mode = value.Mode.ToString(),
                value.TimeZoneId,
                value.IsActive
            })
            .ToArrayAsync(cancellationToken);
        return Results.Ok(communities);
    }

    private static async Task<IResult> CreateCommunityAsync(
        HttpRequest request,
        CreateCommunityRequest body,
        AppDbContext dbContext,
        TelegramMiniAppAuthenticator authenticator,
        IOptions<CampOptions> campOptions,
        CancellationToken cancellationToken)
    {
        var identity = AuthenticateAdmin(request, authenticator, campOptions);
        if (identity.Result is not null) return identity.Result;

        BotCommunity community;
        try
        {
            community = CommunityOptions.CreateValidated(
                body.Key,
                body.Name,
                body.TelegramChatId,
                body.Mode,
                body.TimeZoneId);
        }
        catch (InvalidOperationException exception)
        {
            return Results.BadRequest(new { message = exception.Message });
        }

        if (community.Mode == BotMode.Camp)
        {
            return Results.BadRequest(new { message = "Создайте кэмп через /admin и штатный выбор группы Telegram." });
        }

        if (await dbContext.OyinQCommunities.AnyAsync(
                value => value.Key == community.Key || value.TelegramChatId == community.TelegramChatId,
                cancellationToken))
        {
            return Results.Conflict(new { message = "Сообщество с таким ключом или Telegram chat ID уже существует." });
        }

        var now = DateTimeOffset.UtcNow;
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
        dbContext.Clubs.Add(new Club
        {
            BotChat = botChat,
            BotChatKey = community.Key,
            Name = community.Name,
            CollectionJson = ClubCollectionSerializer.Serialize(ClubCollectionDocument.Empty),
            CreatedAt = now,
            UpdatedAt = now
        });
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Results.Conflict(new { message = "Сообщество с таким ключом или Telegram chat ID уже существует." });
        }

        return Results.Created($"/api/miniapp/admin/communities/{community.Key}", new { community.Key });
    }

    private static async Task<IResult> GetCommunitiesAsync(
        HttpRequest request,
        TelegramMiniAppAuthenticator authenticator,
        CommunityContextResolver resolver,
        CancellationToken cancellationToken)
    {
        var identity = authenticator.Authenticate(request.Headers["X-Telegram-Init-Data"].FirstOrDefault());
        if (identity is null) return Results.Unauthorized();

        var communities = await resolver.ResolveAuthorizedAsync(identity.TelegramUserId, cancellationToken);
        return Results.Ok(communities.Select(community => new
        {
            community.Key,
            community.Name,
            Mode = community.Mode.ToString()
        }));
    }

    private static async Task<IResult> GetGamesAsync(
        HttpRequest request,
        string community,
        AppDbContext dbContext,
        TelegramMiniAppAuthenticator authenticator,
        CommunityContextResolver resolver,
        CancellationToken cancellationToken)
    {
        var access = await AuthorizeAsync(request, community, authenticator, resolver, cancellationToken);
        if (access.Result is not null) return access.Result;
        var modeAccess = await EnsureModeAccessAsync(dbContext, access, cancellationToken);
        if (modeAccess is not null) return modeAccess;

        if (access.Community!.Mode == BotMode.Club)
        {
            var json = await dbContext.Clubs.AsNoTracking()
                .Where(value => value.BotChatKey == access.Community.Key)
                .Select(value => value.CollectionJson)
                .SingleAsync(cancellationToken);
            return Results.Ok(ClubCollectionSerializer.Deserialize(json).Games.Select(game =>
                new CatalogGameResponse(
                    game.BggId,
                    game.BggId,
                    game.Name,
                    game.ThumbnailImageUrl,
                    game.Expansions.ToArray(),
                    "catalog",
                    1)));
        }

        var camp = await dbContext.Camps.AsNoTracking()
            .Include(value => value.Contributions)
            .SingleAsync(value => value.BotChatKey == access.Community.Key, cancellationToken);
        var contributedExpansions = camp.Contributions
            .Where(value => value.ItemType == CampContributionItemType.Expansion)
            .Where(value => value.ParentBggId.HasValue)
            .GroupBy(value => value.ParentBggId!.Value)
            .ToDictionary(
                group => group.Key,
                group => group.GroupBy(value => value.BggId).Select(value =>
                {
                    using var json = System.Text.Json.JsonDocument.Parse(value.First().SnapshotJson);
                    return new ClubCollectionExpansion(value.Key, json.RootElement.GetProperty("name").GetString() ?? "Без названия");
                }).ToArray());
        var baseGames = camp.ReadBaseCollection().Games.Select(game => new CatalogGameResponse(
            game.BggId,
            game.BggId,
            game.Name,
            game.ThumbnailImageUrl,
            game.Expansions.Concat(contributedExpansions.GetValueOrDefault(game.BggId) ?? [])
                .DistinctBy(value => value.BggId)
                .ToArray(),
            "catalog",
            1 + camp.Contributions.Where(value => value.ItemType == CampContributionItemType.BaseGame && value.BggId == game.BggId)
                .Select(value => value.ParticipantId)
                .Distinct()
                .Count()));
        var contributedGames = camp.Contributions
            .Where(value => value.ItemType == CampContributionItemType.BaseGame)
            .GroupBy(value => value.BggId)
            .Select(group =>
            {
                using var json = System.Text.Json.JsonDocument.Parse(group.First().SnapshotJson);
                var thumbnail = json.RootElement.TryGetProperty("thumbnailImageUrl", out var thumbnailValue)
                    ? thumbnailValue.GetString()
                    : null;
                return new CatalogGameResponse(
                    group.Key,
                    group.Key,
                    json.RootElement.GetProperty("name").GetString() ?? "Без названия",
                    thumbnail,
                    contributedExpansions.GetValueOrDefault(group.Key) ?? [],
                    "catalog",
                    group.Select(value => value.ParticipantId).Distinct().Count());
            });
        return Results.Ok(baseGames.Concat(contributedGames).DistinctBy(value => value.BggId).OrderBy(value => value.Name));
    }

    private static async Task<IResult> GetBggGameAsync(
        HttpRequest request,
        string community,
        string input,
        TelegramMiniAppAuthenticator authenticator,
        CommunityContextResolver resolver,
        IBoardGameGeekClient bggClient,
        CancellationToken cancellationToken)
    {
        var access = await AuthorizeAsync(request, community, authenticator, resolver, cancellationToken);
        if (access.Result is not null) return access.Result;
        var bggId = BggGameUrlParser.Parse(input);
        if (bggId is null) return Results.BadRequest(new { message = "Вставьте ссылку BGG вида boardgamegeek.com/boardgame/12345/..." });
        var details = await bggClient.GetGameDetailsAsync(bggId.Value, cancellationToken);
        return details is null
            ? Results.NotFound()
            : Results.Ok(new
            {
                BggId = details.Game.BggId,
                details.Game.Name,
                details.Game.ThumbnailImageUrl,
                details.Game.ImageUrl,
                details.Game.MinPlayers,
                details.Game.MaxPlayers,
                details.Game.BestPlayers,
                details.Expansions
            });
    }

    private static async Task<IResult> GetGatheringsAsync(
        HttpRequest request,
        string community,
        bool? canTeachRules,
        AppDbContext dbContext,
        TelegramMiniAppAuthenticator authenticator,
        CommunityContextResolver resolver,
        GatheringPresentationService presentation,
        CancellationToken cancellationToken)
    {
        var access = await AuthorizeAsync(request, community, authenticator, resolver, cancellationToken);
        if (access.Result is not null) return access.Result;
        var modeAccess = await EnsureModeAccessAsync(dbContext, access, cancellationToken);
        if (modeAccess is not null) return modeAccess;

        var query = dbContext.GameGatherings.AsNoTracking()
            .Where(value => value.CommunityKey == access.Community!.Key
                && value.Status != GatheringStatus.Cancelled)
            .Include(value => value.Game)
            .Include(value => value.Participants)
            .OrderBy(value => value.StartsAtUtc)
            .AsQueryable();
        if (canTeachRules.HasValue)
        {
            query = query.Where(value => value.CanTeachRules == canTeachRules.Value);
        }

        var gatherings = await query.ToArrayAsync(cancellationToken);
        return Results.Ok(gatherings.Select(value => presentation.BuildCard(value, access.Community!)));
    }

    private static async Task<IResult> GetGatheringAsync(
        HttpRequest request,
        Guid publicId,
        string community,
        AppDbContext dbContext,
        TelegramMiniAppAuthenticator authenticator,
        CommunityContextResolver resolver,
        GatheringPresentationService presentation,
        CancellationToken cancellationToken)
    {
        var access = await AuthorizeAsync(request, community, authenticator, resolver, cancellationToken);
        if (access.Result is not null) return access.Result;
        var modeAccess = await EnsureModeAccessAsync(dbContext, access, cancellationToken);
        if (modeAccess is not null) return modeAccess;

        var gathering = await dbContext.GameGatherings.AsNoTracking()
            .Include(value => value.Game)
            .Include(value => value.OrganizerParticipant)
            .Include(value => value.Participants)
            .Include(value => value.Expansions)
            .SingleOrDefaultAsync(value => value.PublicId == publicId && value.CommunityKey == access.Community!.Key, cancellationToken);
        if (gathering is null) return Results.NotFound();

        return Results.Ok(new
        {
            Gathering = presentation.BuildDetails(gathering, access.Community!),
            CanEdit = gathering.OrganizerParticipant.TelegramUserId == access.Identity!.TelegramUserId
        });
    }

    private static async Task<IResult> CreateGatheringAsync(
        HttpRequest request,
        CreateGatheringRequest body,
        AppDbContext dbContext,
        TelegramMiniAppAuthenticator authenticator,
        CommunityContextResolver resolver,
        GatheringGameSelectionService gameSelection,
        GatheringTelegramPublisher telegramPublisher,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var access = await AuthorizeAsync(request, body.CommunityKey, authenticator, resolver, cancellationToken);
        if (access.Result is not null) return access.Result;
        var modeAccess = await EnsureModeAccessAsync(dbContext, access, cancellationToken);
        if (modeAccess is not null) return modeAccess;
        var identity = access.Identity!;
        var community = access.Community!;

        var participant = await dbContext.Participants.SingleOrDefaultAsync(
            value => value.TelegramUserId == identity.TelegramUserId,
            cancellationToken);
        if (participant is null)
        {
            participant = new Participant
            {
                TelegramUserId = identity.TelegramUserId,
                TelegramUsername = identity.TelegramUsername,
                DisplayName = identity.DisplayName ?? $"Telegram {identity.TelegramUserId}",
                ActiveCommunityKey = community.Key,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            dbContext.Participants.Add(participant);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        GameGathering gathering;
        try
        {
            if (!DateTime.TryParseExact(
                    body.StartsAtLocal,
                    "yyyy-MM-dd'T'HH:mm",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var localStart))
            {
                return Results.BadRequest(new { message = "Укажите корректные дату и время." });
            }

            localStart = DateTime.SpecifyKind(localStart, DateTimeKind.Unspecified);
            var timeZone = TimeZoneInfo.FindSystemTimeZoneById(community.TimeZoneId);
            if (timeZone.IsInvalidTime(localStart) || timeZone.IsAmbiguousTime(localStart))
            {
                return Results.BadRequest(new { message = "Это локальное время неоднозначно из-за перевода часов. Выберите другое время." });
            }

            var selectedExpansionIds = body.SelectedExpansionIds ?? [];
            GatheringGameSnapshot gameSnapshot;
            if (body.BggId is { } bggId)
            {
                gameSnapshot = string.Equals(body.GameSource, "bgg", StringComparison.OrdinalIgnoreCase)
                    ? await gameSelection.FromArbitraryBggAsync(bggId, selectedExpansionIds, cancellationToken)
                    : community.Mode == BotMode.Club
                        ? await gameSelection.FromClubCollectionAsync(community.Key, bggId, selectedExpansionIds, cancellationToken)
                        : await gameSelection.FromCampCatalogAsync(community.Key, bggId, selectedExpansionIds, cancellationToken);
            }
            else
            {
                return Results.BadRequest(new { message = "Выберите игру." });
            }

            gathering = GatheringRules.Create(
                community.Key,
                gameSnapshot,
                participant.Id,
                new DateTimeOffset(localStart, timeZone.GetUtcOffset(localStart)),
                body.MinimumPlayers,
                body.DesiredPlayers,
                body.MaximumPlayers,
                body.Description,
                body.CanTeachRules,
                DateTimeOffset.UtcNow);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or KeyNotFoundException)
        {
            return Results.BadRequest(new { message = exception.Message });
        }

        dbContext.GameGatherings.Add(gathering);
        await dbContext.SaveChangesAsync(cancellationToken);

        var announcementPublished = false;
        try
        {
            await dbContext.Entry(gathering).Reference(value => value.OrganizerParticipant).LoadAsync(cancellationToken);
            var message = await telegramPublisher.PublishAsync(gathering, community, cancellationToken);
            gathering.TelegramChatId = message.Chat.Id;
            gathering.TelegramMessageId = message.Id;
            gathering.UpdatedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
            announcementPublished = true;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            loggerFactory.CreateLogger("GatheringCreation").LogWarning(
                exception,
                "Gathering {GatheringPublicId} was created but its Telegram announcement failed.",
                gathering.PublicId);
        }

        return Results.Created(
            $"/api/miniapp/gatherings/{gathering.PublicId}",
            new { gathering.PublicId, AnnouncementPublished = announcementPublished });
    }

    private static async Task<IResult> UpdatePresentationAsync(
        HttpRequest request,
        Guid publicId,
        UpdateGatheringPresentationRequest body,
        AppDbContext dbContext,
        TelegramMiniAppAuthenticator authenticator,
        CommunityContextResolver resolver,
        GatheringTelegramPublisher telegramPublisher,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var access = await AuthorizeAsync(request, body.CommunityKey, authenticator, resolver, cancellationToken);
        if (access.Result is not null) return access.Result;

        var gathering = await dbContext.GameGatherings
            .Include(value => value.OrganizerParticipant)
            .SingleOrDefaultAsync(value => value.PublicId == publicId && value.CommunityKey == access.Community!.Key, cancellationToken);
        if (gathering is null) return Results.NotFound();
        if (gathering.OrganizerParticipant.TelegramUserId != access.Identity!.TelegramUserId) return Results.Forbid();

        try
        {
            GatheringRules.UpdatePresentation(gathering, body.Description, body.CanTeachRules, DateTimeOffset.UtcNow);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return Results.BadRequest(new { message = exception.Message });
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            await dbContext.Entry(gathering).Collection(value => value.Participants).LoadAsync(cancellationToken);
            await dbContext.Entry(gathering).Collection(value => value.Expansions).LoadAsync(cancellationToken);
            await telegramPublisher.UpdateAsync(gathering, access.Community!, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            loggerFactory.CreateLogger("GatheringPresentationUpdate").LogWarning(
                exception,
                "Gathering {GatheringPublicId} was updated but its Telegram announcement was not.",
                gathering.PublicId);
        }

        return Results.NoContent();
    }

    private static Task<IResult> JoinGatheringAsync(
        HttpRequest request,
        Guid publicId,
        GatheringMutationRequest body,
        TelegramMiniAppAuthenticator authenticator,
        CommunityContextResolver resolver,
        GatheringService gatheringService,
        GatheringTelegramPublisher telegramPublisher,
        CancellationToken cancellationToken) =>
        MutateParticipationAsync(
            request,
            publicId,
            body,
            join: true,
            authenticator,
            resolver,
            gatheringService,
            telegramPublisher,
            cancellationToken);

    private static Task<IResult> LeaveGatheringAsync(
        HttpRequest request,
        Guid publicId,
        GatheringMutationRequest body,
        TelegramMiniAppAuthenticator authenticator,
        CommunityContextResolver resolver,
        GatheringService gatheringService,
        GatheringTelegramPublisher telegramPublisher,
        CancellationToken cancellationToken) =>
        MutateParticipationAsync(
            request,
            publicId,
            body,
            join: false,
            authenticator,
            resolver,
            gatheringService,
            telegramPublisher,
            cancellationToken);

    private static async Task<IResult> MutateParticipationAsync(
        HttpRequest request,
        Guid publicId,
        GatheringMutationRequest body,
        bool join,
        TelegramMiniAppAuthenticator authenticator,
        CommunityContextResolver resolver,
        GatheringService gatheringService,
        GatheringTelegramPublisher telegramPublisher,
        CancellationToken cancellationToken)
    {
        var access = await AuthorizeAsync(request, body.CommunityKey, authenticator, resolver, cancellationToken);
        if (access.Result is not null) return access.Result;

        try
        {
            var gathering = join
                ? await gatheringService.JoinAsync(publicId, access.Community!.Key, access.Identity!.TelegramUserId, DateTimeOffset.UtcNow, cancellationToken)
                : await gatheringService.LeaveAsync(publicId, access.Community!.Key, access.Identity!.TelegramUserId, DateTimeOffset.UtcNow, cancellationToken);
            await telegramPublisher.UpdateAsync(gathering, access.Community, cancellationToken);
            return Results.NoContent();
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
        catch (UnauthorizedAccessException exception)
        {
            return Results.Json(new { message = exception.Message }, statusCode: StatusCodes.Status403Forbidden);
        }
        catch (InvalidOperationException exception)
        {
            return Results.BadRequest(new { message = exception.Message });
        }
    }

    private static async Task<AccessResult> AuthorizeAsync(
        HttpRequest request,
        string communityKey,
        TelegramMiniAppAuthenticator authenticator,
        CommunityContextResolver resolver,
        CancellationToken cancellationToken)
    {
        var identity = authenticator.Authenticate(request.Headers["X-Telegram-Init-Data"].FirstOrDefault());
        if (identity is null) return new(null, null, Results.Unauthorized());

        var community = await resolver.ResolveAuthorizedAsync(communityKey, identity.TelegramUserId, cancellationToken);
        return community is null
            ? new(identity, null, Results.Forbid())
            : new(identity, community, null);
    }

    private static AccessResult AuthenticateAdmin(
        HttpRequest request,
        TelegramMiniAppAuthenticator authenticator,
        IOptions<CampOptions> campOptions)
    {
        var identity = authenticator.Authenticate(request.Headers["X-Telegram-Init-Data"].FirstOrDefault());
        if (identity is null) return new(null, null, Results.Unauthorized());
        return campOptions.Value.AdminTelegramIds.Contains(identity.TelegramUserId)
            ? new(identity, null, null)
            : new(identity, null, Results.Forbid());
    }

    private static async Task<IResult?> EnsureModeAccessAsync(
        AppDbContext dbContext,
        AccessResult access,
        CancellationToken cancellationToken)
    {
        if (access.Community!.Mode == BotMode.Club)
        {
            return null;
        }

        var participantId = await dbContext.Participants.AsNoTracking()
            .Where(value => value.TelegramUserId == access.Identity!.TelegramUserId)
            .Select(value => (long?)value.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (participantId is null)
        {
            return Results.Json(new { message = "Сначала завершите регистрацию в кэмпе." }, statusCode: StatusCodes.Status403Forbidden);
        }

        var registered = await dbContext.CampRegistrations.AsNoTracking().AnyAsync(
            value => value.Camp.BotChatKey == access.Community.Key && value.ParticipantId == participantId,
            cancellationToken);
        return registered
            ? null
            : Results.Json(new { message = "Сначала завершите регистрацию в кэмпе." }, statusCode: StatusCodes.Status403Forbidden);
    }

    private static async Task<Participant> GetOrCreateParticipantAsync(
        AppDbContext dbContext,
        AccessResult access,
        CancellationToken cancellationToken)
    {
        var participant = await dbContext.Participants.SingleOrDefaultAsync(
            value => value.TelegramUserId == access.Identity!.TelegramUserId,
            cancellationToken);
        if (participant is not null) return participant;
        var now = DateTimeOffset.UtcNow;
        participant = new Participant
        {
            TelegramUserId = access.Identity!.TelegramUserId,
            TelegramUsername = access.Identity.TelegramUsername,
            DisplayName = access.Identity.DisplayName ?? $"Telegram {access.Identity.TelegramUserId}",
            ActiveCommunityKey = access.Community!.Key,
            CreatedAt = now,
            UpdatedAt = now
        };
        dbContext.Participants.Add(participant);
        await dbContext.SaveChangesAsync(cancellationToken);
        return participant;
    }

    private sealed record AccessResult(TelegramMiniAppIdentity? Identity, BotCommunity? Community, IResult? Result);
    public sealed record CreateGatheringRequest(
        string CommunityKey,
        long? BggId,
        string? GameSource,
        long[]? SelectedExpansionIds,
        string StartsAtLocal,
        int MinimumPlayers,
        int DesiredPlayers,
        int MaximumPlayers,
        string? Description,
        bool CanTeachRules);
    public sealed record UpdateGatheringPresentationRequest(string CommunityKey, string? Description, bool CanTeachRules);
    public sealed record GatheringMutationRequest(string CommunityKey);
    public sealed record CampRegistrationRequest(
        string CommunityKey,
        int DaysStaying,
        bool NeedsAccommodation,
        string? DisplayName);
    public sealed record CampImportPreviewRequest(string CommunityKey, string BggInput);
    public sealed record SaveCampContributionsRequest(
        string CommunityKey,
        IReadOnlyList<CampImportSelectionItem> Items);
    public sealed record AddClubGameRequest(long BggId, long[]? ExpansionBggIds);
    public sealed record CampManualGameRequest(string CommunityKey, string BggInput, long[]? ExpansionBggIds);
    private sealed record CatalogGameResponse(
        long Id,
        long BggId,
        string Name,
        string? ThumbnailImageUrl,
        IReadOnlyList<ClubCollectionExpansion> Expansions,
        string Source,
        int ContributorCount);
    public sealed record CreateCommunityRequest(
        string Key,
        string Name,
        long TelegramChatId,
        string Mode,
        string TimeZoneId,
        long? SourceClubId = null);
}
