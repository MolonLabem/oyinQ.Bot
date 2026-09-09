namespace oyinQ.Bot.Features.Communities;

public sealed class CommunityMembershipUnavailableException(Exception innerException)
    : Exception("Не удалось проверить участие в сообществе: Telegram временно недоступен. Попробуйте ещё раз позже.", innerException);
