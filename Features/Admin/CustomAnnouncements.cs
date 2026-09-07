using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data.Entities;

namespace oyinQ.Bot.Features.Admin;

public sealed record AnnouncementHistoryItem(string Id, string Text, DateTimeOffset CreatedAt);
public sealed record AnnouncementHistory(IReadOnlyList<AnnouncementHistoryItem> Items, bool HasNext);

public sealed partial class ReleaseAnnouncementService
{
    public const int MaxMessageLength = 3500;
    private const string CustomPrefix = "custom-";

    private static string CustomId(Guid requestId)
    {
        if (requestId == Guid.Empty) throw new ArgumentException("Некорректный идентификатор сообщения.");
        return CustomPrefix + requestId.ToString("N");
    }

    // Saving freezes the reviewed text, but creates no delivery work.
    public async Task<string> SaveCustomAsync(long telegramId, Guid requestId, string text, CancellationToken ct)
    {
        RequireSuperAdmin(telegramId);
        var id = CustomId(requestId);
        text = text?.Replace("\r\n", "\n").Replace('\r', '\n').Trim() ?? "";
        if (text.Length is 0 or > MaxMessageLength || text.Contains('\0'))
            throw new ArgumentException($"Введите сообщение от 1 до {MaxMessageLength} символов.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var participantId = await db.Participants.Where(x => x.TelegramUserId == telegramId).Select(x => x.Id).SingleAsync(ct);
        if (db.Database.IsRelational())
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"ReleaseAnnouncements\" (\"Id\", \"Text\", \"CreatedByParticipantId\", \"CreatedAt\") VALUES ({id}, {text}, {participantId}, {clock.GetUtcNow()}) ON CONFLICT (\"Id\") DO NOTHING", ct);
            await db.ReleaseAnnouncements.FromSqlInterpolated($"SELECT * FROM \"ReleaseAnnouncements\" WHERE \"Id\" = {id} FOR UPDATE").SingleAsync(ct);
        }
        var saved = await db.ReleaseAnnouncements.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (saved is null)
            db.ReleaseAnnouncements.Add(new() { Id = id, Text = text, CreatedByParticipantId = participantId, CreatedAt = clock.GetUtcNow() });
        else if (saved.Text != text || saved.CreatedByParticipantId != participantId)
            throw new InvalidOperationException("Сообщение уже сохранено с другим текстом. Откройте его в истории или создайте новое сообщение.");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return id;
    }

    public async Task<ReleasePreview> PreviewCustomAsync(long telegramId, Guid requestId, CancellationToken ct)
    {
        RequireSuperAdmin(telegramId);
        var id = CustomId(requestId);
        var message = await db.ReleaseAnnouncements.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new KeyNotFoundException("Сообщение не найдено.");
        return await PreviewContentAsync(id, message.Text, ct);
    }

    public async Task QueueCustomAsync(long telegramId, Guid requestId, IReadOnlyCollection<string> keys,
        bool confirmed, bool retryFailed, CancellationToken ct)
    {
        RequireSuperAdmin(telegramId);
        if (!confirmed || keys is null || keys.Count is 0 or > 200)
            throw new ArgumentException("Подтвердите сообщение и выберите от 1 до 200 сообществ.");
        var preview = await PreviewCustomAsync(telegramId, requestId, ct);
        await QueueContentAsync(telegramId, preview, keys, retryFailed, ct);
    }

    public async Task<AnnouncementHistory> CustomHistoryAsync(long telegramId, int page, CancellationToken ct)
    {
        RequireSuperAdmin(telegramId);
        if (page is < 1 or > 100000) throw new ArgumentException("Некорректный номер страницы.");
        var rows = await db.ReleaseAnnouncements.AsNoTracking().Where(x => x.Id.StartsWith(CustomPrefix))
            .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Skip((page - 1) * 20).Take(21)
            .Select(x => new AnnouncementHistoryItem(x.Id, x.Text, x.CreatedAt)).ToArrayAsync(ct);
        return new(rows.Take(20).ToArray(), rows.Length > 20);
    }
}
