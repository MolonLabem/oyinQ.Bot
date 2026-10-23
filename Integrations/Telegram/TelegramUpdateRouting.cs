using Telegram.Bot;

namespace oyinQ.Bot.Integrations.Telegram;

public static class TelegramUpdateRouting
{
    public static bool IsGroupEntryRequest(string? text, string? command) =>
        command is "/oiynq" or "/oyinq";

    public static bool IsPostingTopicSelectionRequest(string? text, string? command)
    {
        if (command is not ("/oiynq" or "/oyinq") || string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 && string.Equals(parts[1], "topic", StringComparison.OrdinalIgnoreCase);
    }

    public static async Task<string?> GetCommandAsync(string? text, ITelegramBotClient botClient,
        CancellationToken cancellationToken)
    {
        var token = CommandToken(text);
        if (token is null) return null;
        var username = token.Contains('@') ? (await botClient.GetMe(cancellationToken)).Username : null;
        return GetCommand(text, username);
    }

    public static string? GetCommand(string? text, string? botUsername = null)
    {
        var token = CommandToken(text);
        if (token is null) return null;
        var parts = token.Split('@', 2);
        if (parts.Length == 2 && (string.IsNullOrWhiteSpace(botUsername)
            || !string.Equals(parts[1], botUsername, StringComparison.OrdinalIgnoreCase))) return null;
        return parts[0].ToLowerInvariant();
    }

    private static string? CommandToken(string? text) =>
        string.IsNullOrWhiteSpace(text) || text[0] != '/' ? null
            : text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries)[0];
}
