using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Integrations.Telegram;

namespace oyinQ.Bot.Features.Gatherings;

public sealed record GatheringParticipantNameChange(Guid ParticipantId, string? DisplayNameOverride);

public static class GatheringParticipantNames
{
    public const int MaxLength = 128;

    public static string? Normalize(string? value)
    {
        value = value?.Trim();
        if (string.IsNullOrEmpty(value)) return null;
        if (value.Length > MaxLength || value.Any(char.IsControl))
            throw new ArgumentException($"Имя в этой партии должно быть одной строкой длиной до {MaxLength} символов.");
        return value;
    }

    public static string? GetOverride(GameGathering gathering, Participant participant) =>
        participant.DeletedAt is not null ? null : participant.Id == gathering.OrganizerParticipantId ? gathering.OrganizerDisplayNameOverride
            : gathering.Participants.SingleOrDefault(x => x.ParticipantId == participant.Id)?.DisplayNameOverride;

    public static string GetDisplayName(GameGatheringParticipant participation) =>
        participation.Participant.DeletedAt is not null ? ParticipantPresentation.AnonymousName
            : participation.DisplayNameOverride ?? ParticipantPresentation.GetDisplayName(participation.Participant);

    public static string GetDisplayName(GameGathering gathering, Participant participant) =>
        participant.DeletedAt is not null ? ParticipantPresentation.AnonymousName
            : GetOverride(gathering, participant) ?? ParticipantPresentation.GetDisplayName(participant);

    public static string GetDisplayName(GatheringPlayRecord record, GatheringPlayPlayer player)
    {
        // Guests retain the recorded name; registered players retain their identity and current profile fallback.
        var participant = player.Participant ?? (player.ParticipantId == record.Gathering.OrganizerParticipantId
            ? record.Gathering.OrganizerParticipant
            : record.Gathering.Participants.SingleOrDefault(x => x.ParticipantId == player.ParticipantId)?.Participant);
        return participant is null ? player.DisplayName : GetDisplayName(record.Gathering, participant);
    }

    public static bool Apply(GameGathering gathering, IReadOnlyCollection<GatheringParticipantNameChange>? changes)
    {
        if (changes is null || changes.Count == 0) return false;
        if (changes.Select(x => x.ParticipantId).Distinct().Count() != changes.Count)
            throw new ArgumentException("Имя каждого участника можно указать только один раз.");
        var roster = gathering.Participants.Where(x => x.Status is GatheringParticipationStatus.Confirmed or GatheringParticipationStatus.Waitlisted)
            .Select(x => x.Participant).Prepend(gathering.OrganizerParticipant).DistinctBy(x => x.Id)
            .Where(x => x.DeletedAt == null)
            .ToDictionary(x => x.PublicId);
        var normalized = changes.Select(x => (Participant: roster.GetValueOrDefault(x.ParticipantId)
                ?? throw new ArgumentException("Участник не найден в составе этого сбора."), Name: Normalize(x.DisplayNameOverride)))
            .ToArray();
        var changed = false;
        foreach (var (participant, name) in normalized)
        {
            if (GetOverride(gathering, participant) == name) continue;
            if (participant.Id == gathering.OrganizerParticipantId) gathering.OrganizerDisplayNameOverride = name;
            else gathering.Participants.Single(x => x.ParticipantId == participant.Id).DisplayNameOverride = name;
            changed = true;
        }
        return changed;
    }
}
