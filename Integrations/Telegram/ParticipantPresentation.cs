using System.Net;
using oyinQ.Bot.Data.Entities;

namespace oyinQ.Bot.Integrations.Telegram;

public static class ParticipantPresentation
{
    public const string AnonymousName = "Удалённый профиль";
    public static string GetDisplayName(Participant participant)
    {
        if (participant.DeletedAt is not null) return AnonymousName;
        if (!string.IsNullOrWhiteSpace(participant.PreferredDisplayName))
        {
            return participant.PreferredDisplayName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(participant.DisplayName))
        {
            return participant.DisplayName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(participant.TelegramUsername))
        {
            return participant.TelegramUsername;
        }

        return participant.TelegramUserId.ToString();
    }

    public static string ToHtmlLink(Participant participant, int? maximumNameLength = null, string? displayName = null)
    {
        displayName = participant.DeletedAt is not null ? AnonymousName : displayName ?? GetDisplayName(participant);
        if (maximumNameLength is { } limit && displayName.Length > limit)
        {
            var end = limit - 1;
            if (end > 0 && char.IsHighSurrogate(displayName[end - 1])) end--;
            displayName = displayName[..end] + "…";
        }
        var name = WebUtility.HtmlEncode(displayName);
        return participant.DeletedAt is null && participant.TelegramUserId > 0
            ? $"<a href=\"tg://user?id={participant.TelegramUserId}\">{name}</a>"
            : name;
    }

    public static string? GetContactUrl(Participant participant) =>
        participant.DeletedAt is not null ? null : GetContactUrl(participant.TelegramUserId, participant.TelegramUsername);

    public static string? GetContactUrl(long telegramUserId, string? telegramUsername)
    {
        var username = telegramUsername?.Trim().TrimStart('@');
        if (!string.IsNullOrWhiteSpace(username))
            return $"https://t.me/{Uri.EscapeDataString(username)}?profile";
        return telegramUserId > 0 ? $"tg://user?id={telegramUserId}" : null;
    }
}
