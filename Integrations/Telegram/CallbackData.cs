namespace oyinQ.Bot.Integrations.Telegram;

public readonly record struct CallbackData(string Prefix, string Action, long EntityId)
{
    public override string ToString() => Build(Prefix, Action, EntityId);

    public static string Build(string prefix, string action, long entityId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(entityId);

        if (prefix.Contains(':', StringComparison.Ordinal)
            || action.Contains(':', StringComparison.Ordinal))
        {
            throw new ArgumentException("Callback prefix and action must not contain ':'.");
        }

        return $"{prefix}:{action}:{entityId}";
    }

    public static bool TryParse(string? value, out CallbackData callbackData)
    {
        callbackData = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Split(':', StringSplitOptions.None);
        if (parts is not [var prefix, var action, var entityIdText]
            || string.IsNullOrWhiteSpace(prefix)
            || string.IsNullOrWhiteSpace(action)
            || !long.TryParse(entityIdText, out var entityId)
            || entityId <= 0)
        {
            return false;
        }

        callbackData = new CallbackData(prefix, action, entityId);
        return true;
    }
}
