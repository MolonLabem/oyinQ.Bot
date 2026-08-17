using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Sessions;

namespace oyinQ.Bot.Tests;

public sealed class SessionMessageFormatterTests
{
    private readonly SessionMessageFormatter formatter = new();

    [Fact]
    public void Format_Recruiting_ShowsRemainingPlayersAndParticipants()
    {
        var session = CreateSession(SessionStatus.Recruiting, wantedAdditionalPlayers: 2, includeGuest: true);

        var text = formatter.Format(session);

        Assert.Contains("Нужно ещё: 1", text);
        Assert.Contains("Организатор: Host", text);
        Assert.Contains("• Host (организатор)", text);
        Assert.Contains("• Guest", text);
    }

    [Fact]
    public void Format_Full_ShowsFullStatus()
    {
        var session = CreateSession(SessionStatus.Full, wantedAdditionalPlayers: 1, includeGuest: true);

        var text = formatter.Format(session);

        Assert.Contains("✅ Состав набран", text);
        Assert.Contains("Игроки: 2/2", text);
    }

    [Fact]
    public void Format_Cancelled_UsesCancelledTextAndEscapesNames()
    {
        var session = CreateSession(SessionStatus.Closed, wantedAdditionalPlayers: 1, includeGuest: false);
        session.Game.Name = "Game <One>";
        session.HostParticipant.DisplayName = "A&B";

        var text = formatter.Format(session, cancelled: true);

        Assert.Contains("❌ Сбор отменён", text);
        Assert.Contains("Game &lt;One&gt;", text);
        Assert.Contains("A&amp;B", text);
    }

    private static GameSession CreateSession(
        SessionStatus status,
        int wantedAdditionalPlayers,
        bool includeGuest)
    {
        var host = new Participant { Id = 1, DisplayName = "Host" };
        var session = new GameSession
        {
            Id = 10,
            GameId = 20,
            HostParticipantId = host.Id,
            Game = new Game { Id = 20, Name = "Test Game" },
            HostParticipant = host,
            WantedAdditionalPlayers = wantedAdditionalPlayers,
            Status = status,
            Participants =
            [
                new GameSessionParticipant
                {
                    Id = 100,
                    ParticipantId = host.Id,
                    Participant = host,
                    JoinedAt = DateTimeOffset.UtcNow
                }
            ]
        };

        if (includeGuest)
        {
            var guest = new Participant { Id = 2, DisplayName = "Guest" };
            session.Participants.Add(new GameSessionParticipant
            {
                Id = 101,
                ParticipantId = guest.Id,
                Participant = guest,
                JoinedAt = DateTimeOffset.UtcNow.AddSeconds(1)
            });
        }

        return session;
    }
}
