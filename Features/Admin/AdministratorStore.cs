using System.Data;
using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;

namespace oyinQ.Bot.Features.Admin;

public sealed record AdministratorRecord(
    long TelegramUserId,
    string? DisplayName,
    string? TelegramUsername,
    long? AddedByTelegramUserId,
    DateTimeOffset CreatedAt);

public interface IAdministratorStore
{
    Task<bool> IsAdministratorAsync(long telegramUserId, CancellationToken cancellationToken);
    Task<IReadOnlyList<AdministratorRecord>> ListAsync(CancellationToken cancellationToken);
    Task AddAsync(long telegramUserId, string? displayName, string? telegramUsername,
        long addedByTelegramUserId, CancellationToken cancellationToken);
    Task RemoveAsync(long telegramUserId, CancellationToken cancellationToken);
}

public sealed class AdministratorStore(AppDbContext dbContext) : IAdministratorStore
{
    public Task<bool> IsAdministratorAsync(long telegramUserId, CancellationToken cancellationToken) =>
        dbContext.OyinQAdministrators.AsNoTracking().AnyAsync(
            value => value.TelegramUserId == telegramUserId,
            cancellationToken);

    public async Task<IReadOnlyList<AdministratorRecord>> ListAsync(CancellationToken cancellationToken) =>
        await dbContext.OyinQAdministrators.AsNoTracking()
            .OrderBy(value => value.CreatedAt)
            .ThenBy(value => value.TelegramUserId)
            .Select(value => new AdministratorRecord(
                value.TelegramUserId,
                value.DisplayName,
                value.TelegramUsername,
                value.AddedByTelegramUserId,
                value.CreatedAt))
            .ToArrayAsync(cancellationToken);

    public async Task AddAsync(
        long telegramUserId,
        string? displayName,
        string? telegramUsername,
        long addedByTelegramUserId,
        CancellationToken cancellationToken)
    {
        if (telegramUserId <= 0)
        {
            throw new InvalidOperationException("Telegram user ID must be positive.");
        }

        if (await IsAdministratorAsync(telegramUserId, cancellationToken)) return;

        dbContext.OyinQAdministrators.Add(new OyinQAdministrator
        {
            TelegramUserId = telegramUserId,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim(),
            TelegramUsername = string.IsNullOrWhiteSpace(telegramUsername) ? null : telegramUsername.Trim(),
            AddedByTelegramUserId = addedByTelegramUserId,
            CreatedAt = DateTimeOffset.UtcNow
        });
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            dbContext.ChangeTracker.Clear();
            if (!await IsAdministratorAsync(telegramUserId, cancellationToken)) throw;
        }
    }

    public async Task RemoveAsync(long telegramUserId, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var administrators = await dbContext.OyinQAdministrators
            .OrderBy(value => value.TelegramUserId)
            .ToArrayAsync(cancellationToken);
        var administrator = administrators.SingleOrDefault(value => value.TelegramUserId == telegramUserId);
        if (administrator is null) return;
        if (administrators.Length == 1)
        {
            throw new InvalidOperationException("Нельзя удалить последнего администратора.");
        }

        dbContext.OyinQAdministrators.Remove(administrator);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
