using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;

namespace oyinQ.Bot.Features.Collections;

// Reads committed source documents on every request, on every application instance.
// Links convey collection contents only; callers still authorize the consuming community.
public sealed class SharedCollectionReader(AppDbContext db)
{
    public async Task<Club> SourceAsync(long clubId, CancellationToken ct, long? forbiddenId = null)
    {
        var visited = new HashSet<long>();
        while (true)
        {
            if (clubId == forbiddenId || !visited.Add(clubId))
                throw new InvalidOperationException("Нельзя создать замкнутую связь коллекций.");
            var club = await db.Clubs.AsNoTracking().SingleOrDefaultAsync(x => x.Id == clubId, ct)
                ?? throw new KeyNotFoundException("Клуб-источник не найден.");
            if (club.SourceClubId is not { } next) return club;
            clubId = next;
        }
    }

    public async Task<ClubCollectionDocument> ForCampAsync(Camp camp, CancellationToken ct) =>
        camp.SourceClubId is { } source
            ? (await SourceAsync(source, ct)).ReadCollection()
            : camp.ReadBaseCollection();
}
