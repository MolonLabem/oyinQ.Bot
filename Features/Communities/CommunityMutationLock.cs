using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;

namespace oyinQ.Bot.Features.Communities;

// Creation and deletion take this lock before enumerating or inserting gatherings.
public static class CommunityMutationLock
{
    public static async Task<OyinQCommunity> AcquireAsync(AppDbContext db, string key, CancellationToken ct)
    {
        var query = db.Database.IsRelational()
            ? db.OyinQCommunities.FromSqlInterpolated($"SELECT * FROM \"OyinQCommunities\" WHERE \"Key\" = {key} FOR UPDATE")
            : db.OyinQCommunities.Where(x => x.Key == key);
        // No tracking: an earlier authorization read must not mask the locked database state.
        return await query.AsNoTracking().SingleOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException("Сообщество не найдено.");
    }
}
