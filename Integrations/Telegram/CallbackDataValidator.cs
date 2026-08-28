namespace oyinQ.Bot.Integrations.Telegram;

public static class CallbackDataValidator
{
    public static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var parts = value.Split(':', StringSplitOptions.None);
        return parts is ["admin", var action]
                && action is "menu" or "participants" or "accommodation" or "games" or "stats" or "export"
            || parts is ["admin", "camp", "create"]
            || parts is ["admin", "camp", "source", var source]
                && (source == "none" || long.TryParse(source, out var id) && id > 0);
    }
}
