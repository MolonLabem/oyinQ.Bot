namespace oyinQ.Bot.Integrations.Telegram;

public static class CallbackDataValidator
{
    public static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Split(':', StringSplitOptions.None);
        return parts[0] switch
        {
            "admin" => IsAdmin(parts),
            "reg" => IsRegistration(parts),
            "collection" => IsCollection(parts),
            "interest" => IsInterest(parts),
            "session" => IsSession(parts),
            "game" => IsGame(parts),
            "copy" => IsCopy(parts),
            _ => false
        };
    }

    private static bool IsAdmin(string[] parts) =>
        parts is ["admin", var action]
        && action is "menu"
            or "participants"
            or "accommodation"
            or "games"
            or "top"
            or "club"
            or "stats"
            or "export"
        || parts is ["admin", "camp", "create"]
        || parts is ["admin", "camp", "source", var source]
            && (source == "none" || IsPositiveLong(source));

    private static bool IsRegistration(string[] parts) =>
        parts switch
        {
            ["reg", "edit"] => true,
            ["reg", "profile"] => true,
            ["reg", "payment"] => true,
            ["reg", "name", "skip"] => true,
            ["reg", "days", var days] => int.TryParse(days, out var parsedDays)
                && parsedDays is >= 1 and <= 3,
            ["reg", "accommodation", var value] => value is "yes" or "no",
            _ => false
        };

    private static bool IsCollection(string[] parts) =>
        parts switch
        {
            ["collection", "menu"] => true,
            ["collection", "cancel"] => true,
            ["collection", "add", "single"] => true,
            ["collection", "import", var provider, var target] =>
                provider is "bgg"
                && target is "personal" or "club",
            _ => false
        };

    private static bool IsInterest(string[] parts) =>
        parts is ["interest", "toggle", var gameId]
        && IsPositiveLong(gameId);

    private static bool IsSession(string[] parts) =>
        parts switch
        {
            ["session", "menu"] => true,
            ["session", "search"] => true,
            ["session", "active", var page] => IsNonNegativeInt(page),
            ["session", "view", var sessionId] => IsPositiveLong(sessionId),
            ["session", "list", var scope, var page] =>
                scope is "p" or "m" && IsNonNegativeInt(page),
            ["session", "game", var gameId] => IsPositiveLong(gameId),
            ["session", "create", var gameId, var wanted] =>
                IsPositiveLong(gameId)
                && int.TryParse(wanted, out var parsedWanted)
                && parsedWanted is >= 1 and <= 4,
            ["session", var action, var sessionId] =>
                action is "join" or "leave" or "pjoin" or "pleave" or "close" or "cancel"
                && IsPositiveLong(sessionId),
            _ => false
        };

    private static bool IsGame(string[] parts) =>
        parts switch
        {
            ["game", "menu"] => true,
            ["game", "my", "menu"] => true,
            ["game", "wishlist", "menu"] => true,
            ["game", "wishlist", var scope, var page] =>
                scope is "popular" or "mine" && IsNonNegativeInt(page),
            ["game", "collections", var page] => IsNonNegativeInt(page),
            ["game", "collection", var participantId, var page] =>
                IsPositiveLong(participantId) && IsNonNegativeInt(page),
            ["game", "collectionall", var participantId] => IsPositiveLong(participantId),
            ["game", "list", var filter, var page] =>
                filter is "p" or "b" or "m" && IsNonNegativeInt(page),
            ["game", "wanted", var page] => IsNonNegativeInt(page),
            ["game", "mywanted", var page] => IsNonNegativeInt(page),
            ["game", "my", var filter, var page] =>
                filter is "d" or "b" or "m" && IsNonNegativeInt(page),
            ["game", "search", var scope] => scope is "catalog" or "my",
            ["game", "card", var gameId, var context, var page] =>
                IsPositiveLong(gameId)
                && !string.IsNullOrWhiteSpace(context)
                && IsNonNegativeInt(page),
            ["game", "availability", var gameId, var context, var page] =>
                IsPositiveLong(gameId)
                && !string.IsNullOrWhiteSpace(context)
                && IsNonNegativeInt(page),
            ["game", "add", var bggId] => IsPositiveLong(bggId),
            _ => false
        };

    private static bool IsCopy(string[] parts) =>
        parts is ["copy", var action, var entityId, var status]
        && action is "add" or "confirm" or "set"
        && IsPositiveLong(entityId)
        && status is "b" or "m";

    private static bool IsPositiveLong(string value) =>
        long.TryParse(value, out var parsed) && parsed > 0;

    private static bool IsNonNegativeInt(string value) =>
        int.TryParse(value, out var parsed) && parsed >= 0;
}
