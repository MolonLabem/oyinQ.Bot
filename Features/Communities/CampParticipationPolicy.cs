using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;

namespace oyinQ.Bot.Features.Communities;

public sealed class CampParticipationPolicy(AppDbContext dbContext, TimeProvider timeProvider)
{
    public static bool IsRegistrationComplete(CampRegistration? registration, Camp camp)
    {
        if (registration is null || camp.StartDate is not { } start || camp.EndDate is not { } end
            || registration.NeedsAccommodation is null)
            return false;
        try
        {
            _ = CampRules.ValidateSelectedDates(registration.SelectedDays.Select(x => x.Date).ToArray(),
                start, end);
            _ = CampRules.NormalizeCity(registration.City);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public static bool HasEnded(Camp camp, string timeZoneId, DateTimeOffset now)
    {
        if (camp.EndDate is not { } end) return false;
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        return localDate > end;
    }

    public static void EnsureAcceptsMutations(Camp camp, string timeZoneId, DateTimeOffset now)
    {
        if (camp.Status != CampStatus.Active)
            throw new InvalidOperationException("Кэмп не принимает изменения.");
        if (HasEnded(camp, timeZoneId, now))
            throw new InvalidOperationException("Кэмп уже завершён и не принимает изменения.");
    }

    public async Task<(Camp Camp, CampRegistration Registration)> RequireCompleteRegistrationAsync(
        long campId, long participantId, CancellationToken cancellationToken,
        DateOnly? requiredDate = null)
    {
        var camp = await dbContext.Camps.AsNoTracking().Include(x => x.BotChat)
            .SingleOrDefaultAsync(x => x.Id == campId, cancellationToken)
            ?? throw new KeyNotFoundException("Кэмп не найден.");
        EnsureAcceptsMutations(camp, camp.BotChat.TimeZoneId, timeProvider.GetUtcNow());
        var registration = await dbContext.CampRegistrations.AsNoTracking().Include(x => x.SelectedDays)
            .SingleOrDefaultAsync(x => x.CampId == campId && x.ParticipantId == participantId,
                cancellationToken);
        if (!IsRegistrationComplete(registration, camp))
            throw new UnauthorizedAccessException("Сначала завершите регистрацию в кэмпе.");
        if (requiredDate is { } date && registration!.SelectedDays.All(x => x.Date != date))
            throw new InvalidOperationException(
                $"Вы не отметили {date:dd.MM.yyyy} в своей регистрации на кэмп. Сначала измените регистрацию.");
        return (camp, registration!);
    }
}
