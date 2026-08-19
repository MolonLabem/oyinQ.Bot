using oyinQ.Bot.Data.Entities;

namespace oyinQ.Bot.Features.Sessions;

public enum SessionParticipationResult
{
    Joined,
    Left,
    AlreadyJoined,
    NotJoined,
    HostAlreadyJoined,
    HostCannotLeave,
    Full,
    Closed
}

public sealed record SessionParticipationChange(
    SessionParticipationResult Result,
    bool Changed);

public static class SessionParticipationLogic
{
    public static SessionParticipationChange Apply(
        GameSession session,
        Participant participant,
        bool join,
        DateTimeOffset now)
    {
        if (session.Status == SessionStatus.Closed)
        {
            return new(SessionParticipationResult.Closed, false);
        }

        var existing = session.Participants.SingleOrDefault(value =>
            value.ParticipantId == participant.Id);

        if (participant.Id == session.HostParticipantId)
        {
            return new(
                join ? SessionParticipationResult.HostAlreadyJoined : SessionParticipationResult.HostCannotLeave,
                false);
        }

        if (join)
        {
            if (existing is not null)
            {
                return new(SessionParticipationResult.AlreadyJoined, false);
            }

            if (CurrentPlayerCount(session) >= TotalPlayerCount(session))
            {
                var changed = session.Status != SessionStatus.Full;
                session.Status = SessionStatus.Full;
                if (changed)
                {
                    session.UpdatedAt = now;
                }

                return new(SessionParticipationResult.Full, changed);
            }

            session.Participants.Add(new GameSessionParticipant
            {
                ParticipantId = participant.Id,
                Participant = participant,
                JoinedAt = now
            });
            RecalculateStatus(session);
            session.UpdatedAt = now;
            return new(SessionParticipationResult.Joined, true);
        }

        if (existing is null)
        {
            return new(SessionParticipationResult.NotJoined, false);
        }

        session.Participants.Remove(existing);
        RecalculateStatus(session);
        session.UpdatedAt = now;
        return new(SessionParticipationResult.Left, true);
    }

    public static int CurrentPlayerCount(GameSession session) =>
        session.Participants.Count(value => value.ParticipantId == session.HostParticipantId) > 0
            ? session.Participants.Count
            : session.Participants.Count + 1;

    public static int TotalPlayerCount(GameSession session) => session.WantedAdditionalPlayers + 1;

    public static void RecalculateStatus(GameSession session)
    {
        if (session.Status == SessionStatus.Closed)
        {
            return;
        }

        session.Status = CurrentPlayerCount(session) >= TotalPlayerCount(session)
            ? SessionStatus.Full
            : SessionStatus.Recruiting;
    }
}
