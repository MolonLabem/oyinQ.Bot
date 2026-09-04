using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Communities;

namespace oyinQ.Bot.Features.Gatherings;

public sealed record RecruitmentRequestResult(bool Queued, string Message, DateTimeOffset? AvailableAt = null);

public sealed class RecruitmentDigestService(AppDbContext db, TimeProvider clock)
{
    public static DateTimeOffset? AvailableAt(OyinQCommunity community) =>
        community.LastRecruitmentDigestAt?.AddHours(community.RecruitmentCooldownHours);

    public static string CooldownMessage(DateTimeOffset availableAt, DateTimeOffset now)
    {
        var minutes = Math.Max(1, (int)Math.Ceiling((availableAt - now).TotalMinutes));
        return $"Напоминание о сборах уже отправляли недавно. Следующее можно отправить через {minutes / 60} ч {minutes % 60} мин.";
    }

    public async Task<string?> LatestStatusAsync(string key, CancellationToken ct)
    {
        var row = await db.RecruitmentDigests.AsNoTracking().Where(x => x.CommunityKey == key)
            .OrderByDescending(x => x.RequestedAt).ThenByDescending(x => x.Id).FirstOrDefaultAsync(ct);
        return row?.State switch
        {
            RecruitmentDigestState.Pending or RecruitmentDigestState.Preparing => "Общее напоминание о сборах готовится.",
            RecruitmentDigestState.Delivering => "Общее напоминание отправляется.",
            RecruitmentDigestState.Delivered => "Общее напоминание о сборах отправлено.",
            RecruitmentDigestState.Failed => "Не удалось отправить общее напоминание. Организатор может повторить запрос.",
            RecruitmentDigestState.DeliveryUnknown => "Результат отправки общего напоминания неизвестен. Автоматического повтора не будет; общий интервал сохраняется.",
            RecruitmentDigestState.Expired => "Напоминание не отправлено: подходящие сборы больше недоступны.",
            _ => null
        };
    }

    public async Task<RecruitmentRequestResult> RequestAsync(string key, Guid gatheringId, long participantId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var community = await CommunityMutationLock.AcquireAsync(db, key, ct);
        await RequireActiveAsync(community, ct);
        var g = await GatheringWriteStore.LockAsync(db, gatheringId, key, ct);
        var now = clock.GetUtcNow();
        if (g.OrganizerParticipantId != participantId) throw new UnauthorizedAccessException("Напомнить может только организатор сбора.");
        if (!GatheringRecruitment.CanRequest(g, participantId, now))
            throw new InvalidOperationException("Напоминание доступно для открытого сбора в ближайшие 36 часов, пока не набран оптимальный состав.");
        if (AvailableAt(community) is { } available && available > now)
            return new(false, CooldownMessage(available, now), available);
        if (await db.RecruitmentDigests.AnyAsync(x => x.CommunityKey == key
            && (x.State == RecruitmentDigestState.Pending || x.State == RecruitmentDigestState.Preparing || x.State == RecruitmentDigestState.Delivering), ct))
            return new(false, "Напоминание о сборах уже готовится.");
        community.LastRecruitmentDigestAt = now;
        Track(community);
        db.RecruitmentDigests.Add(new() { CommunityKey = key, RequestedAt = now });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(true, "Напоминание о ближайших сборах поставлено в очередь.", AvailableAt(community));
    }

    public async Task SetCooldownAsync(string key, int hours, CancellationToken ct)
    {
        if (hours is < 1 or > 24) throw new ArgumentException("Выберите интервал от 1 до 24 часов.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var community = await CommunityMutationLock.AcquireAsync(db, key, ct);
        if (community.DeletedAt is not null) throw new KeyNotFoundException("Сообщество удалено.");
        community.RecruitmentCooldownHours = hours;
        Track(community);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task<bool> IsActiveAsync(OyinQCommunity community, CancellationToken ct)
    {
        if (!community.IsActive || community.DeletedAt is not null) return false;
        if (community.Mode != Common.Options.BotMode.Camp) return true;
        var camp = await db.Camps.AsNoTracking().SingleAsync(x => x.BotChatKey == community.Key, ct);
        return camp.Status == CampStatus.Active && !CampParticipationPolicy.HasEnded(camp, community.TimeZoneId, clock.GetUtcNow());
    }

    private async Task RequireActiveAsync(OyinQCommunity community, CancellationToken ct)
    {
        if (!await IsActiveAsync(community, ct)) throw new InvalidOperationException("Сообщество больше не принимает сборы.");
    }

    private void Track(OyinQCommunity community)
    {
        var tracked = db.OyinQCommunities.Local.SingleOrDefault(x => x.Key == community.Key);
        if (tracked is null) db.OyinQCommunities.Update(community);
        else db.Entry(tracked).CurrentValues.SetValues(community);
    }
}
