using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Notifications;

namespace oyinQ.Bot.Features.MiniApp;

internal sealed record NotificationSettings(bool GatheringFull = false, bool GatheringDetailsChanged = true,
    bool OrganizerParticipantLeft = true, bool OrganizerReplacement = true, bool OrganizerBelowMinimum = true,
    bool OrganizerMissingProvider = false, bool ImportCompleted = true, int ReminderLeadMinutes = 0, bool WishlistGathering = true);

internal static class NotificationEndpoints
{
    public static void MapNotificationEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/profile/notifications", GetAsync);
        group.MapPut("/profile/notifications", SaveAsync);
    }
    private static Task<long> Owner(HttpRequest request, TelegramMiniAppAuthenticator auth, AppDbContext db, CancellationToken ct)
    {
        var identity = MiniAppEndpointSupport.Authenticate(request, auth) ?? throw new UnauthorizedAccessException();
        return db.Participants.Where(x => x.TelegramUserId == identity.TelegramUserId).Select(x => x.Id).SingleAsync(ct);
    }
    private static async Task<IResult> GetAsync(HttpRequest request, TelegramMiniAppAuthenticator auth, AppDbContext db, CancellationToken ct)
    {
        var id = await Owner(request, auth, db, ct);
        var p = await db.NotificationPreferences.AsNoTracking().SingleOrDefaultAsync(x => x.ParticipantId == id, ct) ?? new();
        return Results.Ok(new NotificationSettings(p.GatheringFull, p.GatheringDetailsChanged, p.OrganizerParticipantLeft,
            p.OrganizerReplacement, p.OrganizerBelowMinimum, p.OrganizerMissingProvider, p.ImportCompleted, p.ReminderLeadMinutes, p.WishlistGathering));
    }
    private static async Task<IResult> SaveAsync(HttpRequest request, NotificationSettings body, TelegramMiniAppAuthenticator auth, AppDbContext db, CancellationToken ct)
    {
        if (!NotificationPolicy.ReminderPresets.Contains(body.ReminderLeadMinutes)) return MiniAppEndpointSupport.Problem("validation", "Выберите время напоминания из списка.");
        var id = await Owner(request, auth, db, ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (db.Database.IsRelational()) await db.Participants.FromSqlInterpolated(
            $"""SELECT * FROM "Participants" WHERE "Id" = {id} FOR UPDATE""").SingleAsync(ct);
        var p = await db.NotificationPreferences.SingleOrDefaultAsync(x => x.ParticipantId == id, ct);
        if (p is null) { p = new() { ParticipantId = id }; db.NotificationPreferences.Add(p); }
        p.WishlistGathering = body.WishlistGathering;
        p.GatheringFull = body.GatheringFull; p.GatheringDetailsChanged = body.GatheringDetailsChanged;
        p.OrganizerParticipantLeft = body.OrganizerParticipantLeft; p.OrganizerReplacement = body.OrganizerReplacement;
        p.OrganizerBelowMinimum = body.OrganizerBelowMinimum; p.OrganizerMissingProvider = body.OrganizerMissingProvider;
        p.ImportCompleted = body.ImportCompleted; p.ReminderLeadMinutes = body.ReminderLeadMinutes;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.Ok(body);
    }
}
