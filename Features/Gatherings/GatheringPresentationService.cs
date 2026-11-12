using System.Globalization;
using oyinQ.Bot.Features.Catalog;
using oyinQ.Bot.Features.Collections;
using System.Net;
using System.Text;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Integrations.Telegram;
using oyinQ.Bot.Integrations.BoardGameGeek;

namespace oyinQ.Bot.Features.Gatherings;

public sealed record GatheringCardPresentation(
    Guid PublicId,
    string GameName,
    string? ImageUrl,
    string? Description,
    string OrganizerName,
    bool CanTeachRules,
    string RulesText,
    string LocalDateTime,
    int OccupiedSeats,
    int MaximumPlayers,
    string StatusText,
    string? TypeName,
    long? BggId,
    string? BggUrl, RecruitmentState? Recruitment = null, string? CancellationReason = null, ComplexityInfo? ComplexityInfo = null,
    int? MinPlayTimeMinutes = null, int? MaxPlayTimeMinutes = null, string? BoxWarning = null);

public sealed record GatheringDetailPresentation(
    Guid PublicId,
    string GameName,
    string? ImageUrl,
    string? Description,
    bool CanTeachRules,
    string RulesText,
    string OrganizerName,
    string LocalDateTime,
    int OccupiedSeats,
    IReadOnlyList<string> Expansions,
    string StatusText,
    string? TypeName,
    IReadOnlyList<string> TypeNames,
    IReadOnlyList<string> CategoryNames,
    IReadOnlyList<string> MechanicNames,
    long? BggId,
    string? BggUrl, RecruitmentState? Recruitment = null, string? CancellationReason = null, ComplexityInfo? ComplexityInfo = null,
    int? MinPlayTimeMinutes = null, int? MaxPlayTimeMinutes = null);

public sealed record GatheringParticipationPresentation(string Status, int? WaitlistPosition);

public sealed record GatheringAnnouncement(string HtmlText, string? ImageUrl, string? BggUrl);
public sealed record ProfileGatheringPresentation(Guid PublicId, string CommunityKey, string CommunityName,
    BotMode CommunityMode, string GameName, DateTimeOffset StartsAtUtc, string LocalDate,
    string LocalTime, string LocalDateTime, bool IsOrganizer, DateTimeOffset? EstimatedEndsAtUtc = null,
    int? WaitlistPosition = null, string Progress = "upcoming");

public sealed class GatheringPresentationService
{
    private const int CardDescriptionLength = 120;
    private const int AnnouncementDescriptionLength = 220;
    private static readonly CultureInfo RussianCulture = CultureInfo.GetCultureInfo("ru-RU");

    public GatheringCardPresentation BuildCard(GameGathering gathering, BotCommunity community, GameProviderResponse? provider = null)
    {
        var game = ResolveSnapshot(gathering);
        var metadata = Features.Collections.BggTaxonomyCatalog.Present(game);
        return new(
            gathering.PublicId,
            game.Name,
            game.ThumbnailImageUrl ?? game.ImageUrl,
            Truncate(gathering.Description, CardDescriptionLength),
            GatheringParticipantNames.GetDisplayName(gathering, gathering.OrganizerParticipant),
            gathering.CanTeachRules,
            RulesText(gathering.CanTeachRules),
            FormatLocalDateTime(gathering.StartsAtUtc, community.TimeZoneId),
            GatheringCapacity.OccupiedSeats(gathering),
            gathering.MaximumPlayers,
            StatusText(gathering),
            metadata.TypeNames.FirstOrDefault(),
            game.BggId,
            BggGameUrl.FromId(game.BggId), Recruitment(gathering),
            gathering.Status == GatheringStatus.Cancelled ? gathering.CancellationReason : null, GameComplexityPresentation.Present(game),
            game.MinPlayTimeMinutes, game.MaxPlayTimeMinutes,
            community.Mode == BotMode.Camp && !GatheringLifecycle.IsTerminal(gathering.Status) ? provider?.Warning : null);
    }

    public GatheringDetailPresentation BuildDetails(GameGathering gathering, BotCommunity community)
    {
        var game = ResolveSnapshot(gathering);
        var metadata = Features.Collections.BggTaxonomyCatalog.Present(game);
        return new(
            gathering.PublicId,
            game.Name,
            game.ImageUrl ?? game.ThumbnailImageUrl,
            gathering.Description,
            gathering.CanTeachRules,
            RulesText(gathering.CanTeachRules),
            GatheringParticipantNames.GetDisplayName(gathering, gathering.OrganizerParticipant),
            FormatLocalDateTime(gathering.StartsAtUtc, community.TimeZoneId),
            GatheringCapacity.OccupiedSeats(gathering),
            gathering.Expansions.OrderBy(value => value.Name).Select(value => value.Name).ToArray(),
            StatusText(gathering),
            metadata.TypeNames.FirstOrDefault(),
            metadata.TypeNames,
            metadata.CategoryNames,
            metadata.MechanicNames,
            game.BggId,
            BggGameUrl.FromId(game.BggId), Recruitment(gathering),
            gathering.Status == GatheringStatus.Cancelled ? gathering.CancellationReason : null, GameComplexityPresentation.Present(game),
            game.MinPlayTimeMinutes, game.MaxPlayTimeMinutes);
    }

    public GatheringAnnouncement BuildTelegramAnnouncement(
        GameGathering gathering,
        BotCommunity community, GameProviderResponse? provider = null)
    {
        var full = BuildTelegramAnnouncement(gathering, community, provider, compact: false);
        // Telegram counts caption characters after parsing entities, not HTML markup or link URLs.
        var visible = WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(full.HtmlText, "<[^>]+>", ""));
        return visible.Length <= 1024 ? full : BuildTelegramAnnouncement(gathering, community, provider, compact: true);
    }

    private GatheringAnnouncement BuildTelegramAnnouncement(
        GameGathering gathering, BotCommunity community, GameProviderResponse? provider, bool compact)
    {
        var game = ResolveSnapshot(gathering);
        var text = new StringBuilder();
        text.Append($"<b>{WebUtility.HtmlEncode(compact ? Truncate(game.Name, 160) : game.Name)}</b>");
        if (GameComplexityPresentation.Present(game) is { } complexity)
        {
            var indicator = complexity.Level switch
            {
                GameComplexity.Light or GameComplexity.MediumLight => "🟢",
                GameComplexity.Medium => "🟡",
                _ => "🔴"
            };
            text.Append($" | {indicator} {complexity.DisplayName}");
        }
        text.AppendLine();
        text.Append($"📅 {WebUtility.HtmlEncode(FormatLocalDateTime(gathering.StartsAtUtc, community.TimeZoneId))}");
        if (GameDurationPresentation.Format(game.MinPlayTimeMinutes, game.MaxPlayTimeMinutes) is { } duration)
            text.Append($" | ⏱ {duration}");
        text.AppendLine();
        text.AppendLine($"👥 {GatheringCapacity.OccupiedSeats(gathering)} из {gathering.MaximumPlayers}");
        text.AppendLine();
        text.AppendLine(gathering.CanTeachRules ? "📖 Объясню правила" : "📖 Опыт с игрой желателен");
        var description = Truncate(gathering.Description, compact ? 120 : AnnouncementDescriptionLength);
        if (description is not null) text.AppendLine(WebUtility.HtmlEncode(description));

        if (gathering.Expansions.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("Дополнения:");
            if (compact) text.AppendLine($"Выбрано: {gathering.Expansions.Count}");
            else foreach (var expansion in gathering.Expansions.OrderBy(value => value.Name))
            {
                text.AppendLine($"• {WebUtility.HtmlEncode(expansion.Name)}");
            }
        }

        text.AppendLine();
        text.AppendLine($"Собирает {ParticipantPresentation.ToHtmlLink(gathering.OrganizerParticipant, compact ? 80 : null, GatheringParticipantNames.GetDisplayName(gathering, gathering.OrganizerParticipant))}");
        var confirmed = gathering.Participants.Where(value => value.Status == GatheringParticipationStatus.Confirmed
                && value.Participant is not null)
            .OrderBy(value => value.JoinedAt).ThenBy(value => value.Id).ToArray();
        if (confirmed.Length > 0 || gathering.Guests.Count > 0)
        {
            text.AppendLine("Участники:");
            if (compact) text.AppendLine($"Игроков без организатора: {confirmed.Length}, гостей: {gathering.Guests.Count}");
            else
            {
                foreach (var participant in confirmed)
                    text.AppendLine($"• {ParticipantPresentation.ToHtmlLink(participant.Participant, displayName: GatheringParticipantNames.GetDisplayName(participant))}");
                foreach (var guest in gathering.Guests.OrderBy(value => value.CreatedAt).ThenBy(value => value.Id))
                    text.AppendLine($"• {WebUtility.HtmlEncode(guest.DisplayName)} <i>(гость)</i>");
            }
        }

        var waitlistedCount = gathering.Participants.Count(value =>
            value.Status == GatheringParticipationStatus.Waitlisted);
        if (waitlistedCount > 0)
        {
            text.AppendLine();
            text.AppendLine($"⏳ В очереди: {waitlistedCount}");
        }

        if (compact) text.AppendLine("\nСостав и дополнения — в сборе.");
        text.AppendLine();
        if (community.Mode == BotMode.Camp && !GatheringLifecycle.IsTerminal(gathering.Status) && provider?.Warning is { } warning)
            text.AppendLine($"📦 {warning}");
        text.AppendLine($"<b>{StatusText(gathering)}</b>");
        if (gathering.Status == GatheringStatus.Cancelled && !string.IsNullOrWhiteSpace(gathering.CancellationReason))
            text.AppendLine($"Причина: {WebUtility.HtmlEncode(Truncate(gathering.CancellationReason, compact ? 120 : 500))}");
        return new GatheringAnnouncement(
            text.ToString(),
            game.ImageUrl ?? game.ThumbnailImageUrl,
            BggGameUrl.FromId(game.BggId));
    }

    public ProfileGatheringPresentation BuildProfileSchedule(GameGathering gathering, BotCommunity community,
        long participantId, DateTimeOffset? now = null)
    {
        var game = ResolveSnapshot(gathering);
        var local = TimeZoneInfo.ConvertTime(gathering.StartsAtUtc,
            TimeZoneInfo.FindSystemTimeZoneById(community.TimeZoneId));
        return new(gathering.PublicId, community.Key, community.Name, community.Mode, game.Name,
            gathering.StartsAtUtc, local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            local.ToString("HH:mm", CultureInfo.InvariantCulture),
            FormatLocalDateTime(gathering.StartsAtUtc, community.TimeZoneId),
            gathering.OrganizerParticipantId == participantId, GatheringPlayTiming.EstimatedEnd(gathering),
            BuildParticipation(gathering, participantId).WaitlistPosition,
            now is null || gathering.StartsAtUtc > now ? "upcoming"
                : GatheringPlayTiming.EstimatedEnd(gathering) > now ? "started" : "awaitingOutcome");
    }

    private static GatheringGameSnapshot ResolveSnapshot(GameGathering gathering)
        => GatheringGameSnapshotSerializer.Deserialize(gathering.GameSnapshotJson);

    private static string RulesText(bool canTeachRules) =>
        canTeachRules ? "Правила объясню" : "Опыт с игрой желателен";

    public static GatheringParticipationPresentation BuildParticipation(GameGathering gathering, long participantId)
    {
        if (gathering.OrganizerParticipantId == participantId) return new("Organizer", null);
        var member = gathering.Participants.SingleOrDefault(x => x.ParticipantId == participantId);
        var position = member?.Status == GatheringParticipationStatus.Waitlisted
            ? gathering.Participants.Where(x => x.Status == GatheringParticipationStatus.Waitlisted)
                .OrderBy(x => x.JoinedAt).ThenBy(x => x.Id).Select((x, index) => new { x.ParticipantId, Position = index + 1 })
                .Single(x => x.ParticipantId == participantId).Position : (int?)null;
        return new(member?.Status.ToString() ?? "None", position);
    }

    private static RecruitmentState? Recruitment(GameGathering gathering) =>
        gathering.Status is GatheringStatus.Recruiting or GatheringStatus.Ready or GatheringStatus.Full
            ? GatheringRecruitment.Describe(gathering) : null;

    public static string FormatLocalDateTime(DateTimeOffset startsAtUtc, string timeZoneId)
    {
        var local = TimeZoneInfo.ConvertTime(startsAtUtc, TimeZoneInfo.FindSystemTimeZoneById(timeZoneId));
        return local.ToString("ddd, d MMMM, HH:mm", RussianCulture);
    }

    private static string StatusText(GameGathering gathering) => gathering.Status switch
    {
        GatheringStatus.Recruiting => "🟡 Идёт набор",
        GatheringStatus.Ready => "✅ Есть места",
        GatheringStatus.Full => "🔒 Свободных мест нет",
        GatheringStatus.Closed => "🔒 Запись закрыта",
        GatheringStatus.Completed => "Запись завершена",
        GatheringStatus.Cancelled => "❌ Сбор отменён",
        _ => "Статус неизвестен"
    };

    private static string? Truncate(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (value.Length <= maximumLength) return value;
        var end = maximumLength - 1;
        if (end > 0 && char.IsHighSurrogate(value[end - 1])) end--;
        return string.Concat(value.AsSpan(0, end).TrimEnd(), "…");
    }
}
