using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Communities;
using oyinQ.Bot.Integrations.Telegram;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types.Enums;

namespace oyinQ.Bot.Features.Gatherings;

// Demand-driven outbox: the worker delivers explicit requests; it never schedules a digest.
public sealed class RecruitmentDigestWorker(IServiceScopeFactory scopes, TimeProvider clock,
    ILogger<RecruitmentDigestWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10), clock);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<RecruitmentDigestDispatcher>().ProcessOneAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception e) { logger.LogError(e, "Recruitment digest iteration failed"); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

public sealed class RecruitmentDigestDispatcher(AppDbContext db, TimeProvider clock,
    RecruitmentDigestService service, ITelegramGroupMessageSender sender, ITelegramBotClient bot,
    ILogger<RecruitmentDigestDispatcher> logger)
{
    public async Task<bool> ProcessOneAsync(CancellationToken ct)
    {
        RecruitmentDigest? row;
        var now = clock.GetUtcNow();
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            var query = db.Database.IsRelational()
                ? db.RecruitmentDigests.FromSqlInterpolated($"""
                    SELECT * FROM "RecruitmentDigests" WHERE "State" = 0
                    OR ("State" IN (1, 2) AND "LeaseExpiresAt" <= {now})
                    ORDER BY "RequestedAt", "Id" FOR UPDATE SKIP LOCKED LIMIT 1
                    """)
                : db.RecruitmentDigests.Where(x => x.State == RecruitmentDigestState.Pending
                    || ((x.State == RecruitmentDigestState.Preparing || x.State == RecruitmentDigestState.Delivering) && x.LeaseExpiresAt <= now))
                    .OrderBy(x => x.RequestedAt).ThenBy(x => x.Id).Take(1);
            row = await query.SingleOrDefaultAsync(ct);
            if (row is null) return false;
            if (row.State == RecruitmentDigestState.Delivering)
            {
                row.State = RecruitmentDigestState.DeliveryUnknown;
                await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return true;
            }
            row.State = RecruitmentDigestState.Preparing;
            row.AttemptId = Guid.NewGuid(); row.LeaseExpiresAt = now.AddMinutes(2);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        var attempt = row.AttemptId;
        var sendStarted = false;
        try
        {
            var community = await db.OyinQCommunities.AsNoTracking().SingleAsync(x => x.Key == row.CommunityKey, ct);
            var games = await LoadGamesAsync(row.CommunityKey, now, ct);
            var username = (await bot.GetMe(ct)).Username;
            if (string.IsNullOrWhiteSpace(username)) throw new InvalidOperationException("Bot username is unavailable.");
            var message = RecruitmentDigestFormatter.Build(games, community.Key, community.TimeZoneId, clock.GetUtcNow(), username);
            if (message.Total == 0 || !await service.IsActiveAsync(community, ct))
            { await FinishAsync(row.Id, attempt, RecruitmentDigestState.Expired, null, ct); return true; }
            var send = await sender.PrepareMessageAsync(community.Key, message.Text, ParseMode.Html, message.Keyboard, ct);
            // A reclaimed preparation must not cross the send boundary with its old token.
            await using (var transaction = await db.Database.BeginTransactionAsync(ct))
            {
                var current = await CommunityMutationLock.AcquireAsync(db, community.Key, ct);
                var locked = await LockAsync(row.Id, ct);
                if (locked.AttemptId != attempt || locked.State != RecruitmentDigestState.Preparing) return true;
                var freshGames = await LoadGamesAsync(community.Key, clock.GetUtcNow(), ct);
                var freshMessage = RecruitmentDigestFormatter.Build(freshGames, community.Key, current.TimeZoneId, clock.GetUtcNow(), username);
                if (!await service.IsActiveAsync(current, ct) || row.RequestedAt.AddHours(36) <= clock.GetUtcNow())
                { locked.State = RecruitmentDigestState.Expired; }
                else if (freshMessage.Text != message.Text || !freshGames.Select(x => x.PublicId).Order().SequenceEqual(games.Select(x => x.PublicId).Order()))
                { locked.State = RecruitmentDigestState.Pending; locked.LeaseExpiresAt = null; }
                else
                {
                    var boundary = clock.GetUtcNow();
                    locked.State = RecruitmentDigestState.Delivering; locked.LeaseExpiresAt = boundary.AddMinutes(2);
                    // Reserve at request time, then start the full interval at the actual send boundary.
                    var trackedCommunity = await db.OyinQCommunities.SingleAsync(x => x.Key == community.Key, ct);
                    trackedCommunity.LastRecruitmentDigestAt = boundary;
                }
                await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
                if (locked.State != RecruitmentDigestState.Delivering) return true;
            }
            sendStarted = true;
            var result = await send(ct);
            await FinishAsync(row.Id, attempt, RecruitmentDigestState.Delivered, result.Id, CancellationToken.None);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Recruitment digest {DigestId} failed at {Stage}", row.Id, sendStarted ? "send" : "preparation");
            var state = sendStarted && e is not ApiRequestException
                ? RecruitmentDigestState.DeliveryUnknown : RecruitmentDigestState.Failed;
            await FinishAsync(row.Id, attempt, state, null, CancellationToken.None);
        }
        return true;
    }

    private Task<GameGathering[]> LoadGamesAsync(string key, DateTimeOffset now, CancellationToken ct) =>
        db.GameGatherings.AsNoTracking().Include(x => x.Participants).Include(x => x.Guests)
            .Where(x => x.CommunityKey == key && x.StartsAtUtc > now && x.StartsAtUtc <= now.AddHours(36)
                && (x.Status == GatheringStatus.Recruiting || x.Status == GatheringStatus.Ready)).ToArrayAsync(ct);

    private async Task<RecruitmentDigest> LockAsync(long id, CancellationToken ct)
    {
        var query = db.Database.IsRelational()
            ? db.RecruitmentDigests.FromSqlInterpolated($"SELECT * FROM \"RecruitmentDigests\" WHERE \"Id\" = {id} FOR UPDATE")
            : db.RecruitmentDigests.Where(x => x.Id == id);
        var current = await query.AsNoTracking().SingleAsync(ct);
        var tracked = db.RecruitmentDigests.Local.SingleOrDefault(x => x.Id == id);
        if (tracked is null) { db.Attach(current); return current; }
        db.Entry(tracked).CurrentValues.SetValues(current); return tracked;
    }

    private async Task FinishAsync(long id, Guid? attempt, RecruitmentDigestState state, int? messageId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Same community -> digest lock order as request and send boundary.
        var key = await db.RecruitmentDigests.Where(x => x.Id == id).Select(x => x.CommunityKey).SingleAsync(ct);
        await CommunityMutationLock.AcquireAsync(db, key, ct);
        var row = await LockAsync(id, ct);
        if (row.AttemptId != attempt || row.State == RecruitmentDigestState.Delivered) return;
        row.State = state; row.LeaseExpiresAt = null; row.TelegramMessageId = messageId;
        if (state == RecruitmentDigestState.Delivered) row.DeliveredAt = clock.GetUtcNow();
        // Definitive failure can be explicitly retried; uncertainty consumes the cooldown.
        if ((state is RecruitmentDigestState.Failed or RecruitmentDigestState.Expired)
            && !await db.RecruitmentDigests.AnyAsync(x => x.CommunityKey == key && x.Id > row.Id, ct))
        {
            var tracked = await db.OyinQCommunities.SingleAsync(x => x.Key == key, ct);
            tracked.LastRecruitmentDigestAt = null;
        }
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
    }
}
