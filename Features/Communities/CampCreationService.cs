using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Collections;

namespace oyinQ.Bot.Features.Communities;

public sealed record CampChatValidation(bool IsUsable, string? Title, string? Error);

public interface ICampChatValidator
{
    Task<CampChatValidation> ValidateAsync(long telegramChatId, CancellationToken cancellationToken);
}

public sealed record CreateCampCommand(
    string Key,
    string Name,
    long TelegramChatId,
    string TimeZoneId,
    long CreatedByTelegramUserId,
    long? SourceClubId);

public sealed class CampCreationService(
    AppDbContext dbContext,
    ICampChatValidator chatValidator)
{
    public async Task<Camp> CreateAsync(CreateCampCommand command, CancellationToken cancellationToken)
    {
        var definition = CommunityOptions.CreateValidated(
            command.Key,
            command.Name,
            command.TelegramChatId,
            nameof(BotMode.Camp),
            command.TimeZoneId);
        if (command.CreatedByTelegramUserId <= 0)
        {
            throw new InvalidOperationException("Camp creator Telegram user ID is required.");
        }

        var chat = await chatValidator.ValidateAsync(command.TelegramChatId, cancellationToken);
        if (!chat.IsUsable)
        {
            throw new InvalidOperationException(chat.Error ?? "Бот не может использовать выбранную группу.");
        }

        if (await dbContext.OyinQCommunities.AnyAsync(
                value => value.Key == definition.Key || value.TelegramChatId == definition.TelegramChatId,
                cancellationToken))
        {
            throw new InvalidOperationException("Этот Telegram-чат или ключ уже назначен клубу либо кэмпу.");
        }

        Club? sourceClub = null;
        if (command.SourceClubId is { } sourceClubId)
        {
            sourceClub = await dbContext.Clubs.AsNoTracking()
                .SingleOrDefaultAsync(value => value.Id == sourceClubId, cancellationToken)
                ?? throw new KeyNotFoundException("Исходный клуб не найден.");
            _ = ClubCollectionSerializer.Deserialize(sourceClub.CollectionJson);
        }

        var now = DateTimeOffset.UtcNow;
        var botChat = new OyinQCommunity
        {
            Key = definition.Key,
            Name = definition.Name,
            TelegramChatId = definition.TelegramChatId,
            Mode = BotMode.Camp,
            TimeZoneId = definition.TimeZoneId,
            CreatedAt = now,
            UpdatedAt = now
        };
        var camp = new Camp
        {
            BotChatKey = definition.Key,
            Name = definition.Name,
            SourceClubId = sourceClub?.Id,
            BaseCollectionJson = sourceClub?.CollectionJson
                ?? ClubCollectionSerializer.Serialize(ClubCollectionDocument.Empty),
            Status = CampStatus.Active,
            CreatedByTelegramUserId = command.CreatedByTelegramUserId,
            CreatedAt = now,
            UpdatedAt = now,
            BotChat = botChat
        };
        dbContext.Camps.Add(camp);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            throw new InvalidOperationException("Этот Telegram-чат или ключ уже назначен клубу либо кэмпу.", exception);
        }

        return camp;
    }
}
