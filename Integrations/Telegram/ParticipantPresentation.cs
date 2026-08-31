using System.Net;
using oyinQ.Bot.Data.Entities;

namespace oyinQ.Bot.Integrations.Telegram;

public static class ParticipantPresentation
{
    public static string GetDisplayName(Participant participant)
    {
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

    public static string ToHtmlLink(Participant participant)
    {
        var name = WebUtility.HtmlEncode(GetDisplayName(participant));
        return participant.TelegramUserId > 0
            ? $"<a href=\"tg://user?id={participant.TelegramUserId}\">{name}</a>"
            : name;
    }

    public static string? GetPublicProfileUrl(Participant participant)
    {
        var username = participant.TelegramUsername?.Trim().TrimStart('@');
        return string.IsNullOrWhiteSpace(username) ? null : $"https://t.me/{Uri.EscapeDataString(username)}?profile";
    }
}
