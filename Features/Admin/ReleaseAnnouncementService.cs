using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Integrations.Telegram;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace oyinQ.Bot.Features.Admin;

public static class ReleaseContent
{
    public const string Id = "2026-09-04";
    public static string Text
    {
        get
        {
            using var stream = typeof(ReleaseContent).Assembly.GetManifestResourceStream("OyinQ.Release." + Id)
                ?? throw new InvalidOperationException("Текст обновления отсутствует.");
            using var reader = new StreamReader(stream);
            var text = reader.ReadToEnd().Trim();
            if (text.Length is 0 or > 3500) throw new InvalidOperationException("Текст обновления слишком длинный.");
            return text;
        }
    }
}
public sealed record ReleaseTarget(string Key, string Name, bool CanPost, ReleaseDeliveryState? State, string? Error,
    bool CanQueue, bool CanRetry);
public sealed record ReleasePreview(string ReleaseId, string Text, IReadOnlyList<ReleaseTarget> Targets);

public sealed class ReleaseAnnouncementService(AppDbContext db, IAdminAuthorizationService authorization,
    ITelegramBotClient bot, ITelegramGroupMessageSender sender, TimeProvider clock, ILogger<ReleaseAnnouncementService>? logger = null)
{
    public static bool CanRetry(ReleaseDeliveryState state) => state == ReleaseDeliveryState.Failed;
    private void RequireSuperAdmin(long telegramId)
    {
        if (!authorization.IsSuperAdmin(telegramId)) throw new UnauthorizedAccessException("Публиковать обновление может только суперадминистратор.");
    }

    public static bool CanPost(ChatMember member, ChatFullInfo chat) => member switch
    {
        ChatMemberOwner => true,
        ChatMemberAdministrator admin => chat.Type != ChatType.Channel || admin.CanPostMessages,
        ChatMemberMember => chat.Type is ChatType.Group or ChatType.Supergroup && chat.Permissions?.CanSendMessages == true,
        ChatMemberRestricted restricted => restricted.IsMember && restricted.CanSendMessages,
        _ => false
    };

    private async Task<bool> CanPostAsync(OyinQCommunity community, long botId, CancellationToken ct)
    {
        if (!community.IsActive || community.DeletedAt is not null) return false;
        try
        {
            return CanPost(await bot.GetChatMember(community.TelegramChatId, botId, ct),
                await bot.GetChat(community.TelegramChatId, ct));
        }
        catch (ApiRequestException) { return false; }
    }

    public async Task<ReleasePreview> PreviewAsync(long telegramId, CancellationToken ct)
    {
        RequireSuperAdmin(telegramId);
        var me = await bot.GetMe(ct);
        var states = await db.ReleaseAnnouncementDeliveries.AsNoTracking().Where(x => x.ReleaseId == ReleaseContent.Id).ToDictionaryAsync(x => x.CommunityKey, ct);
        var priorKeys = states.Keys.ToArray();
        var communities = await db.OyinQCommunities.AsNoTracking().Where(x => x.IsActive && x.DeletedAt == null || priorKeys.Contains(x.Key)).OrderBy(x => x.Name).ToArrayAsync(ct);
        List<ReleaseTarget> targets = [];
        foreach (var c in communities)
        {
            states.TryGetValue(c.Key, out var state);
            var canPost = await CanPostAsync(c, me.Id, ct);
            targets.Add(new(c.Key, c.Name, canPost, state?.State, state?.Error,
                canPost && state is null, canPost && state is not null && CanRetry(state.State)));
        }
        return new(ReleaseContent.Id, ReleaseContent.Text, targets);
    }

    public async Task QueueAsync(long telegramId, string releaseId, IReadOnlyCollection<string> keys, bool confirmed, bool retryFailed, CancellationToken ct)
    {
        RequireSuperAdmin(telegramId);
        if (!confirmed || releaseId != ReleaseContent.Id || keys is null || keys.Count is 0 or > 200)
            throw new ArgumentException("Подтвердите текущий выпуск и выберите от 1 до 200 сообществ.");
        var preview = await PreviewAsync(telegramId, ct);
        var selected = keys.Distinct().ToArray();
        if (selected.Any(key => !preview.Targets.Any(x => x.Key == key && x.CanPost)))
            throw new InvalidOperationException("Состав получателей изменился. Обновите предпросмотр.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var participantId = await db.Participants.Where(x => x.TelegramUserId == telegramId).Select(x => x.Id).SingleAsync(ct);
        if (db.Database.IsRelational())
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"ReleaseAnnouncements\" (\"Id\", \"Text\", \"CreatedByParticipantId\", \"CreatedAt\") VALUES ({releaseId}, {preview.Text}, {participantId}, {clock.GetUtcNow()}) ON CONFLICT (\"Id\") DO NOTHING", ct);
            await db.ReleaseAnnouncements.FromSqlInterpolated($"SELECT * FROM \"ReleaseAnnouncements\" WHERE \"Id\" = {releaseId} FOR UPDATE").SingleAsync(ct);
        }
        var release = await db.ReleaseAnnouncements.SingleOrDefaultAsync(x => x.Id == releaseId, ct);
        if (release is null) db.ReleaseAnnouncements.Add(new() { Id = releaseId, Text = preview.Text, CreatedByParticipantId = participantId, CreatedAt = clock.GetUtcNow() });
        else if (release.Text != preview.Text) throw new InvalidOperationException("Текст опубликованного выпуска изменился. Нужен новый идентификатор выпуска.");
        var rows = await db.ReleaseAnnouncementDeliveries.Where(x => x.ReleaseId == releaseId).ToDictionaryAsync(x => x.CommunityKey, ct);
        foreach (var key in selected)
        {
            if (rows.TryGetValue(key, out var row))
            {
                if (retryFailed && CanRetry(row.State)) { row.State = ReleaseDeliveryState.Pending; row.Error = null; }
            }
            else if (!retryFailed) db.ReleaseAnnouncementDeliveries.Add(new() { ReleaseId = releaseId, CommunityKey = key });
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task<bool> DispatchOneAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        if (db.Database.IsRelational()) await db.ReleaseAnnouncementDeliveries
            .Where(x => x.State == ReleaseDeliveryState.Delivering && x.AttemptedAt < now.AddMinutes(-5))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, ReleaseDeliveryState.DeliveryUnknown)
                .SetProperty(x => x.Error, "Результат отправки неизвестен. Проверьте чат вручную."), ct);
        if (db.Database.IsRelational()) await db.ReleaseAnnouncementDeliveries
            .Where(x => x.State == ReleaseDeliveryState.Preparing && x.AttemptedAt < now.AddMinutes(-5))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, ReleaseDeliveryState.Failed)
                .SetProperty(x => x.Error, "Подготовка прервалась. Можно повторить отправку."), ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var row = db.Database.IsRelational()
            ? await db.ReleaseAnnouncementDeliveries.FromSqlRaw("SELECT * FROM \"ReleaseAnnouncementDeliveries\" WHERE \"State\" = 0 ORDER BY \"ReleaseId\", \"CommunityKey\" FOR UPDATE SKIP LOCKED LIMIT 1").SingleOrDefaultAsync(ct)
            : await db.ReleaseAnnouncementDeliveries.Where(x => x.State == ReleaseDeliveryState.Pending).OrderBy(x => x.ReleaseId).ThenBy(x => x.CommunityKey).FirstOrDefaultAsync(ct);
        if (row is null) return false;
        row.State = ReleaseDeliveryState.Preparing;
        row.AttemptedAt = now;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        var sendStarted = false;
        try
        {
            var community = await db.OyinQCommunities.AsNoTracking().SingleAsync(x => x.Key == row.CommunityKey, ct);
            var me = await bot.GetMe(ct);
            if (!await CanPostAsync(community, me.Id, ct))
            {
                row.State = ReleaseDeliveryState.Failed; row.Error = "Сообщество недоступно для публикации.";
            }
            else
            {
                var release = await db.ReleaseAnnouncements.AsNoTracking().SingleAsync(x => x.Id == row.ReleaseId, ct);
                var url = TelegramBotDeepLinks.BuildMainMiniApp(me.Username ?? "", MiniAppStartParameter.ForCommunity(community.Key));
                var send = await sender.PrepareMessageAsync(community.Key, System.Net.WebUtility.HtmlEncode(release.Text), ParseMode.Html,
                    new InlineKeyboardMarkup(InlineKeyboardButton.WithUrl("Открыть OyinQ", url)), ct);
                ct.ThrowIfCancellationRequested();
                var sendTime = clock.GetUtcNow();
                if (db.Database.IsRelational())
                {
                    var claimed = await db.ReleaseAnnouncementDeliveries.Where(x => x.ReleaseId == row.ReleaseId
                        && x.CommunityKey == row.CommunityKey && x.State == ReleaseDeliveryState.Preparing && x.AttemptedAt == now)
                        .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, ReleaseDeliveryState.Delivering)
                            .SetProperty(x => x.AttemptedAt, sendTime), ct);
                    if (claimed == 0) { db.Entry(row).State = EntityState.Detached; return true; }
                }
                row.State = ReleaseDeliveryState.Delivering;
                row.AttemptedAt = sendTime;
                if (!db.Database.IsRelational()) await db.SaveChangesAsync(ct);
                sendStarted = true;
                var message = await send(ct);
                row.State = ReleaseDeliveryState.Delivered; row.DeliveredAt = clock.GetUtcNow(); row.TelegramMessageId = message.Id; row.Error = null;
            }
        }
        catch (ApiRequestException e) when (e.ErrorCode is >= 400 and < 500) { logger?.LogWarning(e, "Release {ReleaseId} rejected for {CommunityKey}", row.ReleaseId, row.CommunityKey); row.State = ReleaseDeliveryState.Failed; row.Error = "Telegram отклонил публикацию. Можно повторить после устранения причины."; }
        catch (Exception e)
        {
            logger?.LogWarning(e, "Release {ReleaseId} failed for {CommunityKey}; send started: {SendStarted}", row.ReleaseId, row.CommunityKey, sendStarted);
            row.State = sendStarted ? ReleaseDeliveryState.DeliveryUnknown : ReleaseDeliveryState.Failed;
            row.Error = sendStarted ? "Результат отправки неизвестен. Проверьте чат вручную."
                : "Не удалось подготовить публикацию. Можно повторить отправку.";
        }
        if (!sendStarted && db.Database.IsRelational())
        {
            await db.ReleaseAnnouncementDeliveries.Where(x => x.ReleaseId == row.ReleaseId && x.CommunityKey == row.CommunityKey
                && x.State == ReleaseDeliveryState.Preparing && x.AttemptedAt == now)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, row.State).SetProperty(x => x.Error, row.Error), CancellationToken.None);
            db.Entry(row).State = EntityState.Detached;
        }
        else await db.SaveChangesAsync(CancellationToken.None);
        return true;
    }
}

public sealed class ReleaseAnnouncementWorker(IServiceScopeFactory scopes, ILogger<ReleaseAnnouncementWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<ReleaseAnnouncementService>().DispatchOneAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception e) { logger.LogError(e, "Release announcement delivery failed."); }
        }
    }
}
