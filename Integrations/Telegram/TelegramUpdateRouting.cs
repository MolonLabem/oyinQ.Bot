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

    public static string? GetCommand(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text[0] != '/') return null;
        var token = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries)[0];
        return token.Split('@', 2)[0].ToLowerInvariant();
    }
}
