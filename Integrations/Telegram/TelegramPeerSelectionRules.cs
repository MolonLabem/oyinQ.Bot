using oyinQ.Bot.Data.Entities;
using Telegram.Bot.Types.ReplyMarkups;

namespace oyinQ.Bot.Integrations.Telegram;

public enum PeerSelectionDecision { Accept, Replay, WrongOwner, WrongPurpose, Expired, Inactive }

public static class TelegramPeerSelectionRules
{
    public static PeerSelectionDecision Evaluate(PendingTelegramPeerSelection pending, long senderId,
        TelegramPeerSelectionPurpose purpose, DateTimeOffset now)
    {
        if (pending.RequestedByTelegramUserId != senderId) return PeerSelectionDecision.WrongOwner;
        if (pending.Purpose != purpose) return PeerSelectionDecision.WrongPurpose;
        if (pending.Status is TelegramPeerSelectionStatus.Completed or TelegramPeerSelectionStatus.Consumed)
            return PeerSelectionDecision.Replay;
        if (pending.ExpiresAt <= now) return PeerSelectionDecision.Expired;
        return pending.Status == TelegramPeerSelectionStatus.Pending
            ? PeerSelectionDecision.Accept : PeerSelectionDecision.Inactive;
    }

    public static KeyboardButton CreateButton(TelegramPeerSelectionPurpose purpose, int requestId) => purpose switch
    {
        TelegramPeerSelectionPurpose.AddAdministrator => new KeyboardButton("Выбрать пользователей")
        {
            RequestUsers = new KeyboardButtonRequestUsers
            {
                RequestId = requestId, UserIsBot = false, MaxQuantity = 10,
                RequestName = true, RequestUsername = true, RequestPhoto = true
            }
        },
        _ => new KeyboardButton("Выбрать группу")
        {
            RequestChat = new KeyboardButtonRequestChat
            {
                RequestId = requestId, ChatIsChannel = false, BotIsMember = true,
                RequestTitle = true, RequestUsername = true, RequestPhoto = true
            }
        }
    };
}
