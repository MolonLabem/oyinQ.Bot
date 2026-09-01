using System.Globalization;
using System.Net;
using System.Text;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Integrations.Telegram;

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
    int ConfirmedPlayers,
    int MaximumPlayers,
    string StatusText,
    string TypeName);

public sealed record GatheringDetailPresentation(
    Guid PublicId,
    string GameName,
    string? ImageUrl,
    string? Description,
    bool CanTeachRules,
    string RulesText,
    string OrganizerName,
    string LocalDateTime,
    int ConfirmedPlayers,
    IReadOnlyList<string> Expansions,
    string StatusText);

public sealed record GatheringAnnouncement(string HtmlText, string? ImageUrl);

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
            ConfirmedPlayers(gathering),
            gathering.MaximumPlayers,
            StatusText(gathering.Status),
            metadata.TypeName);
    }

    public GatheringDetailPresentation BuildDetails(GameGathering gathering, BotCommunity community)
    {
        var game = ResolveSnapshot(gathering);
        return new(
            gathering.PublicId,
            game.Name,
            game.ImageUrl ?? game.ThumbnailImageUrl,
            gathering.Description,
            gathering.CanTeachRules,
            RulesText(gathering.CanTeachRules),
            DisplayName(gathering.OrganizerParticipant),
            FormatLocalDateTime(gathering.StartsAtUtc, community.TimeZoneId),
            ConfirmedPlayers(gathering),
            gathering.Expansions.OrderBy(value => value.Name).Select(value => value.Name).ToArray(),
            StatusText(gathering.Status));
    }

    public GatheringAnnouncement BuildTelegramAnnouncement(
        GameGathering gathering,
        BotCommunity community)
    {
        var game = ResolveSnapshot(gathering);
        var text = new StringBuilder();
        text.AppendLine($"🎲 <b>{WebUtility.HtmlEncode(game.Name)}</b>");
        text.AppendLine($"📅 {WebUtility.HtmlEncode(FormatLocalDateTime(gathering.StartsAtUtc, community.TimeZoneId))}");
        text.AppendLine($"👥 {ConfirmedPlayers(gathering)} / {gathering.DesiredPlayers}–{gathering.MaximumPlayers}");
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
        if (confirmed.Length > 0)
        {
            text.AppendLine();
            text.AppendLine("Участники:");
            foreach (var participant in confirmed)
                text.AppendLine($"• {ParticipantPresentation.ToHtmlLink(participant.Participant)}");
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
            game.ImageUrl ?? game.ThumbnailImageUrl);
    }

    private static GatheringGameSnapshot ResolveSnapshot(GameGathering gathering)
        => GatheringGameSnapshotSerializer.Deserialize(gathering.GameSnapshotJson);

    private static int ConfirmedPlayers(GameGathering gathering) =>
        1 + gathering.Participants.Count(value => value.Status == GatheringParticipationStatus.Confirmed);

    private static string DisplayName(Participant participant) =>
        participant.PreferredDisplayName ?? participant.DisplayName;

    private static string RulesText(bool canTeachRules) =>
        canTeachRules ? "Могу объяснить правила" : "Опыт с игрой желателен";

    private static string FormatLocalDateTime(DateTimeOffset startsAtUtc, string timeZoneId)
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
