using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Integrations.Telegram;

namespace oyinQ.Bot.Features.Gatherings;

public sealed record PlayPlayerChoice(Guid Id, string Name, long? ParticipantId);
public sealed record RecordPlayCommand(bool WasPlayed, DateTimeOffset? EndedAtUtc, int? DurationMinutes,
    IReadOnlyCollection<Guid> PlayerIds, IReadOnlyCollection<long> ExpansionIds, int ExpectedRevision);

public sealed class GatheringPlayConflictException(string message) : InvalidOperationException(message);

public sealed class GatheringPlayService(AppDbContext db, TimeProvider clock)
{
    public static IReadOnlyList<PlayPlayerChoice> PlayerChoices(GameGathering g) =>
        g.Participants.Where(x => x.Status == GatheringParticipationStatus.Confirmed).Select(x => x.Participant)
            .Prepend(g.OrganizerParticipant).DistinctBy(x => x.Id)
            .Select(x => new PlayPlayerChoice(x.PublicId, ParticipantPresentation.GetDisplayName(x), x.Id))
            .Concat(g.Guests.Select(x => new PlayPlayerChoice(x.PublicId, x.DisplayName, null))).ToArray();

    public static IReadOnlyList<Guid> SuggestedPlayerIds(GameGathering gathering)
    {
        var excluded = gathering.Participants.Where(x => x.AttendanceOutcome is AttendanceOutcome.NoShow or AttendanceOutcome.CancelledInAdvance)
            .Select(x => x.ParticipantId).ToHashSet();
        return PlayerChoices(gathering).Where(x => x.ParticipantId is null || !excluded.Contains(x.ParticipantId.Value)).Select(x => x.Id).ToArray();
    }

    public static void RequireAccess(GameGathering g, long participantId)
    {
        if (!GatheringAccessPolicy.CanRecordPlay(g, participantId))
            throw new UnauthorizedAccessException("Запись партии доступна организатору и подтверждённым участникам завершённого сбора.");
    }

    public IQueryable<GameGathering> Gatherings => db.GameGatherings.Include(x => x.Community)
        .Include(x => x.OrganizerParticipant).Include(x => x.Participants).ThenInclude(x => x.Participant).Include(x => x.Guests);

    public async Task<GatheringPlayRecord?> SaveAsync(Guid publicId, string key, long participantId, RecordPlayCommand command, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (db.Database.IsRelational()) await db.GameGatherings.FromSqlInterpolated(
            $"SELECT * FROM \"GameGatherings\" WHERE \"PublicId\" = {publicId} AND \"CommunityKey\" = {key} FOR UPDATE").SingleOrDefaultAsync(ct);
        var g = await Gatherings.SingleOrDefaultAsync(x => x.PublicId == publicId && x.CommunityKey == key, ct)
            ?? throw new KeyNotFoundException("Сбор не найден.");
        RequireAccess(g, participantId);
        if (g.OrganizerParticipantId != participantId)
            throw new UnauthorizedAccessException("Исход и состав партии подтверждает организатор сбора.");
        if (g.StartsAtUtc > clock.GetUtcNow()) throw new InvalidOperationException("Сбор ещё не начался.");
        if (command.PlayerIds is null || command.ExpansionIds is null) throw new ArgumentException("Укажите игроков и дополнения партии.");
        var now = clock.GetUtcNow();
        var record = await db.GatheringPlayRecords.Include(x => x.Players).SingleOrDefaultAsync(x => x.GatheringId == g.Id, ct);
        if (g.OutcomeRevision != command.ExpectedRevision) throw new GatheringPlayConflictException("Запись уже изменена. Обновите страницу перед сохранением.");
        if (!command.WasPlayed && record is not null)
            throw new GatheringPlayConflictException("Партия уже подтверждена. Нельзя отметить её несостоявшейся.");
        if (!command.WasPlayed)
        {
            g.ConfirmedWasPlayed = false;
            g.OutcomeRecordedAt = now;
            g.OutcomeRecordedByParticipantId = participantId;
            g.OutcomeRevision++;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return null;
        }
        if (command.WasPlayed && (command.EndedAtUtc is null || command.EndedAtUtc < g.StartsAtUtc || command.EndedAtUtc > now))
            throw new ArgumentException("Укажите фактическое время окончания: после начала сбора и не в будущем.");
        if (command.DurationMinutes is <= 0 or > 10080) throw new ArgumentException("Продолжительность должна быть от 1 до 10080 минут.");
        var players = PlayerChoices(g).Where(x => command.PlayerIds.Contains(x.Id)).ToArray();
        if (command.WasPlayed && (players.Length == 0 || command.PlayerIds.Distinct().Count() != players.Length))
            throw new ArgumentException("Выберите фактических игроков из состава сбора.");
        var snapshot = GatheringGameSnapshotSerializer.Deserialize(g.GameSnapshotJson);
        var selectedExpansions = GatheringExpansionSelection.Select(snapshot.SelectedExpansions, command.ExpansionIds);
        if (record is null)
        {
            record = new() { GatheringId = g.Id, RecordedByParticipantId = participantId, RecordedAt = now };
            db.GatheringPlayRecords.Add(record);
        }
        db.GatheringPlayPlayers.RemoveRange(record.Players);
        record.Players.Clear();
        record.WasPlayed = command.WasPlayed;
        record.EndedAtUtc = command.WasPlayed ? command.EndedAtUtc?.ToUniversalTime() : null;
        record.DurationMinutes = command.WasPlayed ? command.DurationMinutes : null;

        record.UpdatedAt = now;
        g.ConfirmedWasPlayed = true;
        g.OutcomeRecordedAt = now;
        g.OutcomeRecordedByParticipantId = participantId;
        record.Revision = ++g.OutcomeRevision;
        record.GameSnapshotJson = GatheringGameSnapshotSerializer.Serialize(snapshot with
        { SelectedExpansions = selectedExpansions });
        if (command.WasPlayed) foreach (var p in players)
            record.Players.Add(new() { SourcePlayerId = p.Id, ParticipantId = p.ParticipantId, DisplayName = p.Name });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return record;
    }
}
