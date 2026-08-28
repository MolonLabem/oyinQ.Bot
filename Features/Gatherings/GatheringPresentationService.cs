using System.Globalization;
using System.Net;
using System.Text;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data.Entities;

namespace oyinQ.Bot.Features.Gatherings;

public sealed record GatheringCardPresentation(
    Guid PublicId,
    string GameName,
    string? ImageUrl,
    string? Description,
    string RulesText,
    string LocalDateTime,
    int ConfirmedPlayers,
    int DesiredPlayers,
    int MaximumPlayers,
    string StatusText);

public sealed record GatheringDetailPresentation(
    Guid PublicId,
    string GameName,
    string? ImageUrl,
    string? Description,
    bool CanTeachRules,
    string RulesText,
    string OrganizerName,
    string LocalDateTime,
    int MinimumPlayers,
    int DesiredPlayers,
    int MaximumPlayers,
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
        return new(
            gathering.PublicId,
            game.Name,
            game.ThumbnailImageUrl ?? game.ImageUrl,
            Truncate(gathering.Description, CardDescriptionLength),
            RulesText(gathering.CanTeachRules),
            FormatLocalDateTime(gathering.StartsAtUtc, community.TimeZoneId),
            ConfirmedPlayers(gathering),
            gathering.DesiredPlayers,
            gathering.MaximumPlayers,
            StatusText(gathering.Status));
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
            gathering.MinimumPlayers,
            gathering.DesiredPlayers,
            gathering.MaximumPlayers,
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
        text.AppendLine($"Организатор: {WebUtility.HtmlEncode(DisplayName(gathering.OrganizerParticipant))}");
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
    {
        if (!string.IsNullOrWhiteSpace(gathering.GameSnapshotJson))
        {
            return GatheringGameSnapshotSerializer.Deserialize(gathering.GameSnapshotJson);
        }

        var game = gathering.Game
            ?? throw new InvalidOperationException("Gathering has neither a game snapshot nor a legacy game.");
        return new GatheringGameSnapshot(
            GatheringGameSnapshot.CurrentVersion,
            game.BggId,
            game.Name,
            game.ThumbnailImageUrl,
            game.ImageUrl,
            game.MinPlayers,
            game.MaxPlayers,
            game.BestPlayers,
            gathering.Expansions.Select(value => new Features.Collections.ClubCollectionExpansion(value.BggId, value.Name)).ToArray());
    }

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
        GatheringStatus.Recruiting => "🟡 Пока не хватает игроков",
        GatheringStatus.Ready => "✅ Мест достаточно",
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
