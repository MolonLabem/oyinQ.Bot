using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;

namespace oyinQ.Bot.Features.Gatherings;

public sealed class ExternalPlayReferenceService(AppDbContext db, TimeProvider clock)
{
    public static string Normalize(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        value = value.Trim();
        if (value.Any(char.IsControl) || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || uri.UserInfo.Length > 0
            || !string.Equals(uri.IdnHost, "app.bgstatsapp.com", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Укажите HTTPS-ссылку BG Stats на app.bgstatsapp.com.");
        var canonical = uri.AbsoluteUri;
        if (canonical.Length > GatheringExternalPlayReference.MaxUrlLength)
            throw new ArgumentException("Ссылка BG Stats слишком длинная после обработки адреса.");
        return canonical;
    }

    public static bool CanShare(GatheringPlayRecord play, long participantId) => play.WasPlayed
        && (play.Gathering.OrganizerParticipantId == participantId || play.Players.Any(x => x.ParticipantId == participantId));

    public static bool CanRemove(GatheringExternalPlayReference reference, long organizerId, long participantId) =>
        reference.AddedByParticipantId == participantId || organizerId == participantId;

    public async Task<GatheringPlayRecord> RequirePlayAsync(Guid gatheringId, string key, long participantId, CancellationToken ct)
    {
        var play = await db.GatheringPlayRecords.Include(x => x.Gathering).Include(x => x.Players)
            .SingleOrDefaultAsync(x => x.Gathering.PublicId == gatheringId && x.Gathering.CommunityKey == key && x.WasPlayed, ct)
            ?? throw new KeyNotFoundException("Подтверждённая партия не найдена.");
        if (!CanShare(play, participantId)) throw new UnauthorizedAccessException("Ссылки доступны организатору и фактическим игрокам партии.");
        return play;
    }

    public async Task AddAsync(Guid gatheringId, string key, long participantId, string url, CancellationToken ct)
    {
        url = Normalize(url);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (db.Database.IsRelational()) await db.GameGatherings.FromSqlInterpolated(
            $"SELECT * FROM \"GameGatherings\" WHERE \"PublicId\" = {gatheringId} AND \"CommunityKey\" = {key} FOR UPDATE").SingleOrDefaultAsync(ct);
        var play = await RequirePlayAsync(gatheringId, key, participantId, ct);
        if (await db.GatheringExternalPlayReferences.AnyAsync(x => x.GatheringPlayRecordId == play.Id && x.Url == url, ct))
            throw new InvalidOperationException("Такая ссылка уже добавлена к партии.");
        db.GatheringExternalPlayReferences.Add(new() { GatheringPlayRecordId = play.Id, Url = url,
            Provider = ExternalPlayProvider.BgStats, AddedByParticipantId = participantId, CreatedAt = clock.GetUtcNow() });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task RemoveAsync(Guid gatheringId, string key, long participantId, long referenceId, CancellationToken ct)
    {
        var row = await db.GatheringExternalPlayReferences.Include(x => x.PlayRecord).ThenInclude(x => x.Gathering)
            .SingleOrDefaultAsync(x => x.Id == referenceId && x.PlayRecord.Gathering.PublicId == gatheringId
                && x.PlayRecord.Gathering.CommunityKey == key, ct);
        if (row is null) return;
        if (!CanRemove(row, row.PlayRecord.Gathering.OrganizerParticipantId, participantId))
            throw new UnauthorizedAccessException("Удалить ссылку может её автор или организатор.");
        db.Remove(row);
        await db.SaveChangesAsync(ct);
    }
}
