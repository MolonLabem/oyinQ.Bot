using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data;

namespace oyinQ.Bot.Integrations.Telegram;

public static class ParticipantWriteStore
{
    public static async Task RequireActiveAsync(AppDbContext db, long participantId, CancellationToken ct)
    {
        var query = db.Database.IsNpgsql()
            ? db.Participants.FromSqlInterpolated($"SELECT * FROM \"Participants\" WHERE \"Id\" = {participantId} FOR UPDATE")
            : db.Participants.Where(x => x.Id == participantId);
        var participant = await query.AsNoTracking().SingleOrDefaultAsync(ct);
        if (participant is null || participant.DeletedAt is not null) throw new ProfileDeletedException();
    }
}
