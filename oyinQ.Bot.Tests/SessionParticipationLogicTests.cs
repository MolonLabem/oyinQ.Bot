using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Sessions;

namespace oyinQ.Bot.Tests;

public sealed class SessionParticipationLogicTests
{
    [Fact]
    public void JoinThenDuplicateJoin_IsIdempotent_AndMarksFull()
    {
        var (session, host, guest) = CreateSession(wantedAdditionalPlayers: 1);

        var first = SessionParticipationLogic.Apply(session, guest, join: true, DateTimeOffset.UtcNow);
        var second = SessionParticipationLogic.Apply(session, guest, join: true, DateTimeOffset.UtcNow.AddSeconds(1));

        Assert.Equal(SessionParticipationResult.Joined, first.Result);
        Assert.True(first.Changed);
        Assert.Equal(SessionStatus.Full, session.Status);
        Assert.Equal(2, session.Participants.Count);
        Assert.Equal(SessionParticipationResult.AlreadyJoined, second.Result);
        Assert.False(second.Changed);
        Assert.Equal(2, session.Participants.Count);
        Assert.Contains(session.Participants, value => value.ParticipantId == host.Id);
    }

    [Fact]
    public void LeaveThenDuplicateLeave_IsIdempotent_AndReopensRecruiting()
    {
        var (session, _, guest) = CreateSession(wantedAdditionalPlayers: 1);
        SessionParticipationLogic.Apply(session, guest, join: true, DateTimeOffset.UtcNow);

        var first = SessionParticipationLogic.Apply(session, guest, join: false, DateTimeOffset.UtcNow.AddSeconds(1));
        var second = SessionParticipationLogic.Apply(session, guest, join: false, DateTimeOffset.UtcNow.AddSeconds(2));

        Assert.Equal(SessionParticipationResult.Left, first.Result);
        Assert.True(first.Changed);
        Assert.Equal(SessionStatus.Recruiting, session.Status);
        Assert.Single(session.Participants);
        Assert.Equal(SessionParticipationResult.NotJoined, second.Result);
        Assert.False(second.Changed);
    }

    [Fact]
    public void HostCannotJoinAgainOrLeave()
    {
        var (session, host, _) = CreateSession(wantedAdditionalPlayers: 2);

        var join = SessionParticipationLogic.Apply(session, host, join: true, DateTimeOffset.UtcNow);
        var leave = SessionParticipationLogic.Apply(session, host, join: false, DateTimeOffset.UtcNow);

        Assert.Equal(SessionParticipationResult.HostAlreadyJoined, join.Result);
        Assert.False(join.Changed);
        Assert.Equal(SessionParticipationResult.HostCannotLeave, leave.Result);
        Assert.False(leave.Changed);
        Assert.Single(session.Participants);
    }

    [Fact]
    public void JoinWhenFull_DoesNotAddParticipant()
    {
        var (session, _, firstGuest) = CreateSession(wantedAdditionalPlayers: 1);
        SessionParticipationLogic.Apply(session, firstGuest, join: true, DateTimeOffset.UtcNow);
        var secondGuest = new Participant { Id = 3, TelegramUserId = 300, DisplayName = "Guest 2" };

        var result = SessionParticipationLogic.Apply(
            session,
            secondGuest,
            join: true,
            DateTimeOffset.UtcNow.AddSeconds(1));

        Assert.Equal(SessionParticipationResult.Full, result.Result);
        Assert.False(result.Changed);
        Assert.DoesNotContain(session.Participants, value => value.ParticipantId == secondGuest.Id);
    }

    private static (GameSession Session, Participant Host, Participant Guest) CreateSession(
        int wantedAdditionalPlayers)
    {
        var host = new Participant { Id = 1, TelegramUserId = 100, DisplayName = "Host" };
        var guest = new Participant { Id = 2, TelegramUserId = 200, DisplayName = "Guest" };
        var session = new GameSession
        {
            Id = 10,
            GameId = 20,
            HostParticipantId = host.Id,
            HostParticipant = host,
            Game = new Game { Id = 20, Name = "Test" },
            WantedAdditionalPlayers = wantedAdditionalPlayers,
            Status = SessionStatus.Recruiting,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Participants =
            [
                new GameSessionParticipant
                {
                    Id = 1000,
                    ParticipantId = host.Id,
                    Participant = host,
                    JoinedAt = DateTimeOffset.UtcNow
                }
            ]
        };

        return (session, host, guest);
    }
}
