namespace oyinQ.Bot.Integrations.Telegram;

public static class TelegramUpdateRouting
{
    public static bool IsAdminCallback(string? data) =>
        data?.StartsWith("admin:", StringComparison.Ordinal) == true;

    public static bool IsGroupEntryRequest(string? text, string? command) =>
        command is "/start" or "/menu" or "/admin"
        || text is "Открыть OyinQ" or "🛠 Админ" or "🛠 Админ-панель";

    public static string? GetCommand(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text[0] != '/') return null;
        var token = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries)[0];
        return token.Split('@', 2)[0].ToLowerInvariant();
    }
}
