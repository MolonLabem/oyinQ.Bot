using System.Net;
using System.Text;
using oyinQ.Bot.Data.Entities;

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
        text.AppendLine($"🎲 <b>{Encode(session.Game.Name)}</b>");
        text.AppendLine($"👤 Организатор: {Encode(session.HostParticipant.DisplayName)}");
        text.AppendLine($"👥 Игроки: {currentPlayers}/{totalPlayers}");

        if (cancelled)
        {
            text.AppendLine("❌ Сбор отменён");
        }
        else
        {
            text.AppendLine(session.Status switch
            {
                SessionStatus.Recruiting => $"Нужно ещё: {neededPlayers}",
                SessionStatus.Full => "✅ Состав набран",
                _ => "✅ Набор закрыт"
            });
        }

        text.AppendLine();
        text.AppendLine("Участники:");
        text.AppendLine($"• {Encode(session.HostParticipant.DisplayName)} (организатор)");
        foreach (var participant in otherParticipants)
        {
            text.AppendLine($"• {Encode(participant.DisplayName)}");
        }

        return text.ToString().TrimEnd();
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
