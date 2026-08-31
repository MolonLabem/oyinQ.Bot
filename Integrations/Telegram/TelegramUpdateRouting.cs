namespace oyinQ.Bot.Integrations.Telegram;

public static class TelegramUpdateRouting
{
    public static bool IsGroupEntryRequest(string? text, string? command) =>
        command == "/oyinq";

    public static string? GetCommand(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text[0] != '/') return null;
        var token = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries)[0];
        return token.Split('@', 2)[0].ToLowerInvariant();
    }
}
