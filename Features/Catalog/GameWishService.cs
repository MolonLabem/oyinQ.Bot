using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Collections;
using oyinQ.Bot.Features.Communities;
using oyinQ.Bot.Integrations.BoardGameGeek;

namespace oyinQ.Bot.Features.Catalog;

public sealed class GameWishService(AppDbContext db, GameCatalogService catalog, IBoardGameGeekClient bgg, TimeProvider clock)
{
    public async Task SetAsync(string key, long participantId, long bggId, bool wished, CancellationToken ct)
    {
        if (bggId <= 0) throw new ArgumentException("Укажите игру BGG.");
        var existing = wished ? await db.GameWishes.AsNoTracking().SingleOrDefaultAsync(x => x.CommunityKey == key && x.ParticipantId == participantId && x.BggId == bggId, ct) : null;
        ClubCollectionGame? game = existing is null ? null : ClubCollectionSerializer.Deserialize(existing.SnapshotJson).Games.Single();
        if (wished && game is null)
        {
            var community = await db.OyinQCommunities.AsNoTracking().SingleAsync(x => x.Key == key, ct);
            var telegramId = await db.Participants.Where(x => x.Id == participantId).Select(x => x.TelegramUserId).SingleAsync(ct);
            var saved = (await catalog.LoadAsync(key, community.Mode, telegramId, ct)).SingleOrDefault(x => x.Game.BggId == bggId);
            if (saved is { IsBaseGame: false }) throw new ArgumentException("В вишлист можно добавить только базовую игру.");
            if (saved is not null) game = saved.Game;
            else
            {
                var details = await bgg.GetGameDetailsAsync(bggId, ct)
                    ?? throw new ArgumentException("Базовая игра не найдена в BGG.");
                if (details.Game.BggId != bggId) throw new InvalidOperationException("BGG вернул другую игру.");
                // A wish is not a collection of available expansions.
                game = BggGameMapper.ToCollectionGame(details.Game, []);
            }
        }
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var current = await CommunityMutationLock.AcquireAsync(db, key, ct);
        if (current.DeletedAt is not null || !current.IsActive) throw new InvalidOperationException("Сообщество недоступно.");
        if (wished && current.Mode == BotMode.Camp)
        {
            var camp = await db.Camps.AsNoTracking().SingleAsync(x => x.BotChatKey == key, ct);
            if (camp.Status != CampStatus.Active || CampParticipationPolicy.HasEnded(camp, current.TimeZoneId, clock.GetUtcNow()))
                throw new InvalidOperationException("Вишлист можно изменять только в действующем кэмпе.");
        }
        var row = await db.GameWishes.SingleOrDefaultAsync(x => x.CommunityKey == key && x.ParticipantId == participantId && x.BggId == bggId, ct);
        if (wished && row is null)
        {
            // A concurrent remove can invalidate the optimistic idempotency check.
            if (game is null) throw new InvalidOperationException("Список изменился. Повторите добавление игры.");
            db.GameWishes.Add(new() { CommunityKey = key, ParticipantId = participantId, BggId = bggId,
                SnapshotJson = ClubCollectionSerializer.Serialize(new(ClubCollectionDocument.CurrentVersion, [game with { Expansions = [] }])), CreatedAt = clock.GetUtcNow() });
        }
        else if (!wished && row is not null) db.GameWishes.Remove(row);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
}
