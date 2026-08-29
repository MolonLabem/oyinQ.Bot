using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Collections;

namespace oyinQ.Bot.Features.Communities;

public sealed record ManagedChatValidation(bool IsUsable, string? Title, string? Username, string? Error);

public interface IManagedChatValidator
{
    Task<ManagedChatValidation> ValidateAsync(long telegramChatId, long requestingAdministratorId,
        CancellationToken cancellationToken);
}

public sealed record CreateClubCommand(string Name, long TelegramChatId, string TimeZoneId,
    long CreatedByTelegramUserId);

public sealed record CreateCampCommand(string Name, long TelegramChatId, string TimeZoneId,
    long CreatedByTelegramUserId, long? SourceClubId, DateOnly StartDate, DateOnly EndDate);

public sealed record UpdateCampCommand(string Name, string TimeZoneId, DateOnly StartDate, DateOnly EndDate);

public sealed class ManagedCommunityService(AppDbContext dbContext, IManagedChatValidator chatValidator,
    TimeProvider timeProvider)
{
    public async Task<Club> CreateClubAsync(CreateClubCommand command, CancellationToken cancellationToken)
    {
        var key = CreateKey("club");
        var definition = CommunityOptions.CreateValidated(key, command.Name, command.TelegramChatId,
            nameof(BotMode.Club), command.TimeZoneId);
        var validation = await ValidateAvailableChatAsync(command.TelegramChatId,
            command.CreatedByTelegramUserId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var community = CreateCommunity(definition, validation.Title, true, now);
        var club = new Club
        {
            BotChat = community, BotChatKey = key, Name = community.Name,
            CollectionJson = ClubCollectionSerializer.Serialize(ClubCollectionDocument.Empty),
            CollectionRevision = 1, CreatedAt = now, UpdatedAt = now
        };
        dbContext.Clubs.Add(club);
        await SaveBindingAsync(cancellationToken);
        return club;
    }

    public async Task<Camp> CreateCampAsync(CreateCampCommand command, CancellationToken cancellationToken)
    {
        _ = CampRules.InclusiveDuration(command.StartDate, command.EndDate);
        var key = CreateKey("camp");
        var definition = CommunityOptions.CreateValidated(key, command.Name, command.TelegramChatId,
            nameof(BotMode.Camp), command.TimeZoneId);
        var validation = await ValidateAvailableChatAsync(command.TelegramChatId,
            command.CreatedByTelegramUserId, cancellationToken);
        Club? sourceClub = null;
        if (command.SourceClubId is { } sourceClubId)
        {
            sourceClub = await dbContext.Clubs.AsNoTracking().Include(x => x.BotChat)
                .SingleOrDefaultAsync(x => x.Id == sourceClubId, cancellationToken)
                ?? throw new KeyNotFoundException("Исходный клуб не найден.");
            _ = sourceClub.ReadCollection();
        }
        var now = timeProvider.GetUtcNow();
        var community = CreateCommunity(definition, validation.Title, false, now);
        var camp = new Camp
        {
            BotChat = community, BotChatKey = key, Name = community.Name,
            SourceClubId = sourceClub?.Id,
            BaseCollectionJson = sourceClub?.CollectionJson ?? ClubCollectionSerializer.Serialize(ClubCollectionDocument.Empty),
            Status = CampStatus.Draft, StartDate = command.StartDate, EndDate = command.EndDate,
            CreatedByTelegramUserId = command.CreatedByTelegramUserId, CreatedAt = now, UpdatedAt = now
        };
        dbContext.Camps.Add(camp);
        await SaveBindingAsync(cancellationToken);
        return camp;
    }

    public async Task SetCampStatusAsync(long campId, CampStatus status, CancellationToken cancellationToken)
    {
        var camp = await dbContext.Camps.Include(x => x.BotChat)
            .SingleOrDefaultAsync(x => x.Id == campId, cancellationToken)
            ?? throw new KeyNotFoundException("Кэмп не найден.");
        if (status == CampStatus.Active && (camp.StartDate is null || camp.EndDate is null))
            throw new InvalidOperationException("Перед активацией задайте даты кэмпа.");
        CampRules.ValidateTransition(camp.Status, status);
        camp.Status = status;
        camp.BotChat.IsActive = status == CampStatus.Active;
        camp.UpdatedAt = camp.BotChat.UpdatedAt = timeProvider.GetUtcNow();
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateCampAsync(long campId, UpdateCampCommand command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Name) || command.Name.Trim().Length > 160)
            throw new InvalidOperationException("Название кэмпа некорректно.");
        var timeZone = CommunityOptions.RequireTimeZone(command.TimeZoneId);
        var duration = CampRules.InclusiveDuration(command.StartDate, command.EndDate);
        var camp = await dbContext.Camps.Include(x => x.BotChat).Include(x => x.Registrations)
            .SingleOrDefaultAsync(x => x.Id == campId, cancellationToken)
            ?? throw new KeyNotFoundException("Кэмп не найден.");
        if (camp.Registrations.Any(x => x.DaysStaying > duration))
            throw new InvalidOperationException("Новый диапазон короче уже подтверждённого срока проживания участника.");
        var gatheringStarts = await dbContext.GameGatherings.AsNoTracking()
            .Where(x => x.CommunityKey == camp.BotChatKey && x.Status != GatheringStatus.Cancelled)
            .Select(x => x.StartsAtUtc).ToArrayAsync(cancellationToken);
        if (gatheringStarts.Any(startsAt =>
            {
                var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(startsAt, timeZone).DateTime);
                return localDate < command.StartDate || localDate > command.EndDate;
            }))
            throw new InvalidOperationException("Новый диапазон не включает один или несколько сборов кэмпа.");
        camp.Name = camp.BotChat.Name = command.Name.Trim();
        camp.BotChat.TimeZoneId = command.TimeZoneId;
        camp.StartDate = command.StartDate;
        camp.EndDate = command.EndDate;
        camp.UpdatedAt = camp.BotChat.UpdatedAt = timeProvider.GetUtcNow();
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<ManagedChatValidation> ValidateAvailableChatAsync(long chatId, long administratorId,
        CancellationToken cancellationToken)
    {
        if (administratorId <= 0) throw new InvalidOperationException("Telegram ID создателя обязателен.");
        var validation = await chatValidator.ValidateAsync(chatId, administratorId, cancellationToken);
        if (!validation.IsUsable) throw new InvalidOperationException(validation.Error ?? "Группа недоступна.");
        if (await dbContext.OyinQCommunities.AnyAsync(x => x.TelegramChatId == chatId, cancellationToken))
            throw new InvalidOperationException("Эта Telegram-группа уже назначена клубу или кэмпу.");
        return validation;
    }

    private static OyinQCommunity CreateCommunity(BotCommunity definition, string? selectedTitle,
        bool active, DateTimeOffset now) => new()
    {
        Key = definition.Key,
        Name = string.IsNullOrWhiteSpace(definition.Name) ? selectedTitle ?? definition.Key : definition.Name,
        TelegramChatId = definition.TelegramChatId, Mode = definition.Mode,
        TimeZoneId = definition.TimeZoneId, IsActive = active, CreatedAt = now, UpdatedAt = now
    };

    private async Task SaveBindingAsync(CancellationToken cancellationToken)
    {
        try { await dbContext.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException exception)
        {
            throw new InvalidOperationException("Эта Telegram-группа уже назначена клубу или кэмпу.", exception);
        }
    }

    private static string CreateKey(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..32];
}
