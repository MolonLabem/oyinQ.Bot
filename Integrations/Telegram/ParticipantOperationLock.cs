using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data;

namespace oyinQ.Bot.Integrations.Telegram;

// A session lock spans endpoint-owned transactions. It fences erasure against requests already in flight.
public sealed class ParticipantOperationLock : IAsyncDisposable
{
    private readonly AppDbContext db;
    private readonly long key;
    private readonly bool relational;
    private ParticipantOperationLock(AppDbContext db, long key, bool relational)
    { this.db = db; this.key = key; this.relational = relational; }

    public static async Task<ParticipantOperationLock> AcquireAsync(AppDbContext db, long telegramUserId, CancellationToken ct)
    {
        var key = BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes($"oyinq-profile:{telegramUserId}")));
        var result = new ParticipantOperationLock(db, key, db.Database.IsNpgsql());
        if (!result.relational) return result;
        await db.Database.OpenConnectionAsync(ct);
        try { await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_lock({key})", ct); }
        catch { await result.DisposeAsync(); throw; }
        return result;
    }

    public async ValueTask DisposeAsync()
    {
        if (!relational) return;
        try { await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_unlock({key})", CancellationToken.None); }
        finally { await db.Database.CloseConnectionAsync(); }
    }
}
