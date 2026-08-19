using System.Net;
using System.Text;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Integrations.Telegram;

namespace oyinQ.Bot.Features.Sessions;

public sealed class SessionMessageFormatter
{
    public string Format(GameSession session, bool cancelled = false)
    {
        var otherParticipants = session.Participants
            .Where(value => value.ParticipantId != session.HostParticipantId)
            .OrderBy(value => value.JoinedAt)
            .Select(value => value.Participant)
            .ToArray();
        var currentPlayers = otherParticipants.Length + 1;
        var totalPlayers = session.WantedAdditionalPlayers + 1;
        var neededPlayers = Math.Max(totalPlayers - currentPlayers, 0);

        var text = new StringBuilder();
        text.AppendLine($"🎲 <b>{WebUtility.HtmlEncode(session.Game.Name)}</b>");
        text.AppendLine();
        text.AppendLine($"👤 Организатор: {ParticipantPresentation.ToHtmlLink(session.HostParticipant)}");
        text.AppendLine($"👥 Состав: {currentPlayers}/{totalPlayers}");
        text.AppendLine();

        if (cancelled)
        {
            text.AppendLine("❌ Сбор отменён");
        }
        else
        {
            text.AppendLine(session.Status switch
            {
                SessionStatus.Recruiting => $"🟢 Нужно ещё игроков: {neededPlayers}",
                SessionStatus.Full => "✅ Состав набран",
                _ => "✅ Набор закрыт"
            });
        }

        text.AppendLine();
        text.AppendLine("👥 Участники:");
        text.AppendLine($"• {ParticipantPresentation.ToHtmlLink(session.HostParticipant)} — организатор");
        foreach (var participant in otherParticipants)
        {
            text.AppendLine($"• {ParticipantPresentation.ToHtmlLink(participant)}");
        }

        return text.ToString().TrimEnd();
    }
}
