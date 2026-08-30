using oyinQ.Bot.Data.Entities;

namespace oyinQ.Bot.Integrations.Telegram;

public static class ParticipantIdentityPolicy
{
    public static bool RefreshTrustedPresentation(Participant participant, string? telegramUsername,
        string? displayName, DateTimeOffset now)
    {
        var normalizedUsername = string.IsNullOrWhiteSpace(telegramUsername) ? null : telegramUsername.Trim();
        var normalizedDisplayName = string.IsNullOrWhiteSpace(displayName)
            ? participant.DisplayName : displayName.Trim();
        if (participant.TelegramUsername == normalizedUsername && participant.DisplayName == normalizedDisplayName)
            return false;
        participant.TelegramUsername = normalizedUsername;
        participant.DisplayName = normalizedDisplayName;
        participant.UpdatedAt = now.ToUniversalTime();
        return true;
    }
}
