using System.Globalization;
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
    string? BggUrl);

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
    string? BggUrl);

public sealed record GatheringAnnouncement(string HtmlText, string? ImageUrl, string? BggUrl);
public sealed record ProfileGatheringPresentation(Guid PublicId, string CommunityKey, string CommunityName,
    BotMode CommunityMode, string GameName, DateTimeOffset StartsAtUtc, string LocalDate,
    string LocalTime, string LocalDateTime, bool IsOrganizer);

public sealed class GatheringPresentationService
{
    private const int CardDescriptionLength = 120;
    private const int AnnouncementDescriptionLength = 220;
    private static readonly CultureInfo RussianCulture = CultureInfo.GetCultureInfo("ru-RU");

    public GatheringCardPresentation BuildCard(GameGathering gathering, BotCommunity community)
    {
        var game = ResolveSnapshot(gathering);
        var metadata = Features.Collections.BggTaxonomyCatalog.Present(game);
        return new(
            gathering.PublicId,
            game.Name,
            game.ThumbnailImageUrl ?? game.ImageUrl,
            Truncate(gathering.Description, CardDescriptionLength),
            ParticipantPresentation.GetDisplayName(gathering.OrganizerParticipant),
            gathering.CanTeachRules,
            RulesText(gathering.CanTeachRules),
            FormatLocalDateTime(gathering.StartsAtUtc, community.TimeZoneId),
            GatheringCapacity.OccupiedSeats(gathering),
            gathering.MaximumPlayers,
            StatusText(gathering.Status),
            metadata.TypeNames.FirstOrDefault(),
            game.BggId,
            BggGameUrl.FromId(game.BggId));
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
            ParticipantPresentation.GetDisplayName(gathering.OrganizerParticipant),
            FormatLocalDateTime(gathering.StartsAtUtc, community.TimeZoneId),
            GatheringCapacity.OccupiedSeats(gathering),
            gathering.Expansions.OrderBy(value => value.Name).Select(value => value.Name).ToArray(),
            StatusText(gathering.Status),
            metadata.TypeNames.FirstOrDefault(),
            metadata.TypeNames,
            metadata.CategoryNames,
            metadata.MechanicNames,
            game.BggId,
            BggGameUrl.FromId(game.BggId));
    }

    public GatheringAnnouncement BuildTelegramAnnouncement(
        GameGathering gathering,
        BotCommunity community)
    {
        var game = ResolveSnapshot(gathering);
        var metadata = Features.Collections.BggTaxonomyCatalog.Present(game);
        var text = new StringBuilder();
        text.AppendLine($"🎲 <b>{WebUtility.HtmlEncode(game.Name)}</b>");
        if (metadata.TypeNames.FirstOrDefault() is { } typeName)
            text.AppendLine($"🏷 {WebUtility.HtmlEncode(typeName)}");
        text.AppendLine($"📅 {WebUtility.HtmlEncode(FormatLocalDateTime(gathering.StartsAtUtc, community.TimeZoneId))}");
        text.AppendLine($"👥 {GatheringCapacity.OccupiedSeats(gathering)} / {gathering.DesiredPlayers}–{gathering.MaximumPlayers}");
        text.AppendLine($"Организатор: {ParticipantPresentation.ToHtmlLink(gathering.OrganizerParticipant)}");
        text.AppendLine(gathering.CanTeachRules ? "📖 Правила объясню" : "🎯 Опыт с игрой желателен");

        if (gathering.Expansions.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("Дополнения:");
            foreach (var expansion in gathering.Expansions.OrderBy(value => value.Name))
            {
                text.AppendLine($"• {WebUtility.HtmlEncode(expansion.Name)}");
            }
        }

        var confirmed = gathering.Participants.Where(value => value.Status == GatheringParticipationStatus.Confirmed
                && value.Participant is not null)
            .OrderBy(value => value.JoinedAt).ThenBy(value => value.Id).ToArray();
        if (confirmed.Length > 0 || gathering.Guests.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("Участники:");
            foreach (var participant in confirmed)
                text.AppendLine($"• {ParticipantPresentation.ToHtmlLink(participant.Participant)}");
            foreach (var guest in gathering.Guests.OrderBy(value => value.CreatedAt).ThenBy(value => value.Id))
                text.AppendLine($"• {WebUtility.HtmlEncode(guest.DisplayName)} <i>(гость)</i>");
        }

        var waitlistedCount = gathering.Participants.Count(value =>
            value.Status == GatheringParticipationStatus.Waitlisted);
        if (waitlistedCount > 0)
        {
            text.AppendLine();
            text.AppendLine($"⏳ Лист ожидания: {waitlistedCount}");
        }

        var description = Truncate(gathering.Description, AnnouncementDescriptionLength);
        if (description is not null)
        {
            text.AppendLine();
            text.AppendLine(WebUtility.HtmlEncode(description));
        }

        text.AppendLine();
        text.Append(StatusText(gathering.Status));
        return new GatheringAnnouncement(
            text.ToString(),
            game.ImageUrl ?? game.ThumbnailImageUrl,
            BggGameUrl.FromId(game.BggId));
    }

    public ProfileGatheringPresentation BuildProfileSchedule(GameGathering gathering, BotCommunity community,
        long participantId)
    {
        var game = ResolveSnapshot(gathering);
        var local = TimeZoneInfo.ConvertTime(gathering.StartsAtUtc,
            TimeZoneInfo.FindSystemTimeZoneById(community.TimeZoneId));
        return new(gathering.PublicId, community.Key, community.Name, community.Mode, game.Name,
            gathering.StartsAtUtc, local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            local.ToString("HH:mm", CultureInfo.InvariantCulture),
            FormatLocalDateTime(gathering.StartsAtUtc, community.TimeZoneId),
            gathering.OrganizerParticipantId == participantId);
    }

    private static GatheringGameSnapshot ResolveSnapshot(GameGathering gathering)
        => GatheringGameSnapshotSerializer.Deserialize(gathering.GameSnapshotJson);

    private static string RulesText(bool canTeachRules) =>
        canTeachRules ? "Могу объяснить правила" : "Опыт с игрой желателен";

    public static string FormatLocalDateTime(DateTimeOffset startsAtUtc, string timeZoneId)
    {
        var local = TimeZoneInfo.ConvertTime(startsAtUtc, TimeZoneInfo.FindSystemTimeZoneById(timeZoneId));
        return local.ToString("d MMMM, HH:mm", RussianCulture);
    }

    private static string StatusText(GatheringStatus status) => status switch
    {
        GatheringStatus.Recruiting => "🟡 Идёт набор",
        GatheringStatus.Ready => "✅ Есть места",
        GatheringStatus.Full => "🔒 Свободных мест нет",
        GatheringStatus.Closed => "🔒 Запись закрыта",
        GatheringStatus.Completed => "✅ Сбор завершён",
        GatheringStatus.Cancelled => "❌ Сбор отменён",
        _ => "Статус неизвестен"
    };

    private static string? Truncate(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Length <= maximumLength
            ? value
            : string.Concat(value.AsSpan(0, maximumLength - 1).TrimEnd(), "…");
    }
}
