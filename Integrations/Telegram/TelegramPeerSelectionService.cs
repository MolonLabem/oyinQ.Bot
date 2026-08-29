using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Admin;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace oyinQ.Bot.Integrations.Telegram;

public sealed record SelectedTelegramUser(long TelegramUserId, string? DisplayName, string? Username);
public sealed record SelectedTelegramChat(long TelegramChatId, string? Title, string? Username);
public sealed record TelegramPeerSelectionResult(
    IReadOnlyList<SelectedTelegramUser>? Users,
    SelectedTelegramChat? Chat);
public sealed record TelegramPeerSelectionTicket(
    Guid PublicId,
    TelegramPeerSelectionPurpose Purpose,
    TelegramPeerSelectionStatus Status,
    string? PreparedButtonId,
    DateTimeOffset ExpiresAt,
    TelegramPeerSelectionResult? Result);

public sealed class TelegramPeerSelectionService(
    AppDbContext dbContext,
    IAdministratorStore administratorStore,
    ITelegramBotClient botClient,
    TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<TelegramPeerSelectionTicket> CreateAsync(
        long administratorId, TelegramPeerSelectionPurpose purpose, CancellationToken cancellationToken)
    {
        if (!await administratorStore.IsAdministratorAsync(administratorId, cancellationToken))
            throw new UnauthorizedAccessException("Доступ запрещён.");
        var requestId = await CreateRequestIdAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        var pending = new PendingTelegramPeerSelection
        {
            PublicId = Guid.NewGuid(), RequestId = requestId, RequestedByTelegramUserId = administratorId,
            Purpose = purpose, Status = TelegramPeerSelectionStatus.Pending,
            CreatedAt = now, ExpiresAt = now.AddMinutes(10)
        };
        dbContext.PendingTelegramPeerSelections.Add(pending);
        await dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            var prepared = await botClient.SavePreparedKeyboardButton(
                administratorId, TelegramPeerSelectionRules.CreateButton(purpose, requestId), cancellationToken);
            pending.PreparedButtonId = prepared.Id;
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            dbContext.PendingTelegramPeerSelections.Remove(pending);
            await dbContext.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        return ToTicket(pending);
    }

    public async Task<TelegramPeerSelectionTicket> GetAsync(Guid publicId, long administratorId,
        CancellationToken cancellationToken)
    {
        var pending = await RequireOwnedAsync(publicId, administratorId, cancellationToken);
        if (pending.Status == TelegramPeerSelectionStatus.Pending && pending.ExpiresAt <= timeProvider.GetUtcNow())
        {
            pending.Status = TelegramPeerSelectionStatus.Expired;
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        return ToTicket(pending);
    }

    public async Task<bool> CompleteUsersAsync(int requestId, long senderId,
        IReadOnlyCollection<SharedUser> users, CancellationToken cancellationToken)
    {
        var result = new TelegramPeerSelectionResult(users.Select(user => new SelectedTelegramUser(
            user.UserId,
            string.Join(' ', new[] { user.FirstName, user.LastName }.Where(x => !string.IsNullOrWhiteSpace(x))),
            user.Username)).ToArray(), null);
        return await CompleteAsync(requestId, senderId, TelegramPeerSelectionPurpose.AddAdministrator,
            result, cancellationToken);
    }

    public Task<bool> CompleteChatAsync(int requestId, long senderId, ChatShared chat,
        CancellationToken cancellationToken) => CompleteAsync(requestId, senderId, null,
            new TelegramPeerSelectionResult(null,
                new SelectedTelegramChat(chat.ChatId, chat.Title, chat.Username)), cancellationToken);

    public async Task<TelegramPeerSelectionResult> ConsumeAsync(Guid publicId, long administratorId,
        TelegramPeerSelectionPurpose purpose, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var pending = await dbContext.PendingTelegramPeerSelections
            .FromSqlInterpolated($"SELECT * FROM \"PendingTelegramPeerSelections\" WHERE \"PublicId\" = {publicId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken) ?? throw new KeyNotFoundException("Запрос выбора не найден.");
        EnsureOwner(pending, administratorId);
        if (pending.Purpose != purpose) throw new InvalidOperationException("Запрос выбора имеет другое назначение.");
        if (pending.ExpiresAt <= timeProvider.GetUtcNow()) throw new InvalidOperationException("Запрос выбора истёк.");
        if (pending.Status == TelegramPeerSelectionStatus.Consumed)
            throw new InvalidOperationException("Запрос выбора уже использован.");
        if (pending.Status != TelegramPeerSelectionStatus.Completed)
            throw new InvalidOperationException("Telegram ещё не вернул выбранный объект.");
        var result = ReadResult(pending);
        pending.Status = TelegramPeerSelectionStatus.Consumed;
        pending.ConsumedAt = timeProvider.GetUtcNow();
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task SendFallbackAsync(Guid publicId, long administratorId, CancellationToken cancellationToken)
    {
        var pending = await RequireOwnedAsync(publicId, administratorId, cancellationToken);
        if (pending.Status != TelegramPeerSelectionStatus.Pending || pending.ExpiresAt <= timeProvider.GetUtcNow())
            throw new InvalidOperationException("Запрос выбора больше не активен.");
        await botClient.SendMessage(administratorId,
            "Выберите объект через штатный интерфейс Telegram.",
            replyMarkup: new ReplyKeyboardMarkup([[TelegramPeerSelectionRules.CreateButton(pending.Purpose, pending.RequestId)]])
            { ResizeKeyboard = true, OneTimeKeyboard = true }, cancellationToken: cancellationToken);
    }

    private async Task<bool> CompleteAsync(int requestId, long senderId,
        TelegramPeerSelectionPurpose? exactPurpose, TelegramPeerSelectionResult result,
        CancellationToken cancellationToken)
    {
        var pending = await dbContext.PendingTelegramPeerSelections.SingleOrDefaultAsync(x => x.RequestId == requestId,
            cancellationToken);
        if (pending is null) return false;
        if (pending.RequestedByTelegramUserId != senderId) return true;
        if (exactPurpose is not null && pending.Purpose != exactPurpose) return true;
        if (exactPurpose is null && pending.Purpose == TelegramPeerSelectionPurpose.AddAdministrator) return true;
        if (pending.Status is TelegramPeerSelectionStatus.Completed or TelegramPeerSelectionStatus.Consumed) return true;
        if (pending.Status != TelegramPeerSelectionStatus.Pending || pending.ExpiresAt <= timeProvider.GetUtcNow())
        {
            pending.Status = TelegramPeerSelectionStatus.Expired;
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        pending.ResultJson = JsonSerializer.Serialize(result, JsonOptions);
        pending.Status = TelegramPeerSelectionStatus.Completed;
        pending.CompletedAt = timeProvider.GetUtcNow();
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<int> CreateRequestIdAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var value = RandomNumberGenerator.GetInt32(1, int.MaxValue);
            if (!await dbContext.PendingTelegramPeerSelections.AnyAsync(x => x.RequestId == value, cancellationToken))
                return value;
        }
        throw new InvalidOperationException("Не удалось создать уникальный Telegram request ID.");
    }

    private async Task<PendingTelegramPeerSelection> RequireOwnedAsync(Guid publicId, long administratorId,
        CancellationToken cancellationToken)
    {
        var pending = await dbContext.PendingTelegramPeerSelections.SingleOrDefaultAsync(x => x.PublicId == publicId,
            cancellationToken) ?? throw new KeyNotFoundException("Запрос выбора не найден.");
        EnsureOwner(pending, administratorId);
        return pending;
    }

    private static void EnsureOwner(PendingTelegramPeerSelection pending, long administratorId)
    {
        if (pending.RequestedByTelegramUserId != administratorId)
            throw new UnauthorizedAccessException("Запрос выбора принадлежит другому администратору.");
    }

    private static TelegramPeerSelectionTicket ToTicket(PendingTelegramPeerSelection pending) =>
        new(pending.PublicId, pending.Purpose, pending.Status, pending.PreparedButtonId, pending.ExpiresAt,
            pending.ResultJson is null ? null : ReadResult(pending));

    private static TelegramPeerSelectionResult ReadResult(PendingTelegramPeerSelection pending) =>
        JsonSerializer.Deserialize<TelegramPeerSelectionResult>(pending.ResultJson ?? string.Empty, JsonOptions)
        ?? throw new InvalidOperationException("Результат Telegram-выбора повреждён.");
}
