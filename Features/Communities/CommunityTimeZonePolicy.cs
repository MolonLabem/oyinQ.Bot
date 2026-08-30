namespace oyinQ.Bot.Features.Communities;

public static class CommunityTimeZonePolicy
{
    public static void EnsureChangeAllowed(string currentTimeZoneId, string requestedTimeZoneId,
        bool hasGatherings)
    {
        if (hasGatherings && !string.Equals(currentTimeZoneId, requestedTimeZoneId,
                StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Часовой пояс нельзя изменить после создания первого сбора.");
    }
}
