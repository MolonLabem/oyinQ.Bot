using System.Globalization;
using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data;
using oyinQ.Bot.Features.MiniApp;
using oyinQ.Bot.Integrations.Telegram;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace oyinQ.Bot.Features.Admin;

public sealed record CampAdminParticipant(
    long ParticipantId, string DisplayName, string? City, IReadOnlyList<DateOnly> SelectedDates,
    bool? NeedsAccommodation, string? TelegramUsername, string? ContactUrl)
{
    public int DayCount => SelectedDates.Count;
    public string DatesText => SelectedDates.Count == 0 ? "Не указаны"
        : string.Join(", ", SelectedDates.Select(CampParticipantFilter.FormatDate));
    public string AccommodationText => NeedsAccommodation switch { true => "Нужно", false => "Не нужно", _ => "Не указано" };
}

public sealed record CampAdminParticipants(long CampId, string CampName, IReadOnlyList<CampAdminParticipant> Participants)
{
    public string CommunityKey { get; init; } = "";
    public string TimeZoneId { get; init; } = "UTC";
    public int TotalCount { get; init; } = Participants.Count;
    public IReadOnlyList<DateOnly> AvailableDates { get; init; } = [];
    public string FilterDescription { get; init; } = "Все участники";
}

public sealed record CampParticipantFilter(string? Search = null, DateOnly? AttendanceDate = null, string? Accommodation = null)
{
    public CampParticipantFilter Normalize()
    {
        var search = Search?.Trim();
        if (search?.Length > 200) throw new ArgumentException("Поисковый запрос не должен превышать 200 символов.");
        var accommodation = string.IsNullOrEmpty(Accommodation) ? null : Accommodation;
        if (accommodation is not (null or "needed" or "not-needed" or "unanswered"))
            throw new ArgumentException("Выберите вариант жилья из списка.");
        return this with { Search = string.IsNullOrEmpty(search) ? null : search, Accommodation = accommodation };
    }

    public bool Matches(CampAdminParticipant participant) =>
        (Search is null || participant.DisplayName.Contains(Search, StringComparison.OrdinalIgnoreCase)
            || (participant.City?.Contains(Search, StringComparison.OrdinalIgnoreCase) ?? false)
            || (participant.TelegramUsername is { } username && ("@" + username).Contains(Search, StringComparison.OrdinalIgnoreCase)))
        && (AttendanceDate is null || participant.SelectedDates.Contains(AttendanceDate.Value))
        && (Accommodation switch
        {
            "needed" => participant.NeedsAccommodation == true,
            "not-needed" => participant.NeedsAccommodation == false,
            "unanswered" => participant.NeedsAccommodation is null,
            _ => true
        });

    public string Describe()
    {
        var parts = new List<string>();
        if (Search is not null) parts.Add($"Поиск: {Search}");
        if (AttendanceDate is { } date) parts.Add($"Дата: {FormatDate(date)}");
        if (Accommodation is not null) parts.Add("Жильё: " + (Accommodation switch
        { "needed" => "нужно", "not-needed" => "не нужно", _ => "не указано" }));
        return parts.Count == 0 ? "Все участники" : string.Join("; ", parts);
    }

    public static string FormatDate(DateOnly date) => date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
}

public sealed record CampParticipantDmResult(int MessageCount, int ParticipantCount);
public sealed class CampParticipantDeliveryException(string code, string message, Exception? innerException = null) : InvalidOperationException(message, innerException)
{
    public string Code { get; } = code;
}

public sealed class CampParticipantAdminService(
    AppDbContext dbContext,
    IAdminAuthorizationService authorization,
    ITelegramBotClient botClient,
    ILogger<CampParticipantAdminService> logger,
    MiniAppLinkBuilder links,
    TimeProvider timeProvider)
{
    public async Task<CampAdminParticipants> GetAsync(long actorTelegramUserId, long campId,
        CancellationToken cancellationToken, CampParticipantFilter? filter = null)
    {
        if (!await authorization.CanAdministerCampAsync(actorTelegramUserId, campId, cancellationToken))
            throw new UnauthorizedAccessException("Нет доступа к участникам этого кэмпа.");
        filter = (filter ?? new()).Normalize();
        var camp = await dbContext.Camps.AsNoTracking().Where(x => x.Id == campId)
            .Select(x => new { x.Id, x.Name, x.BotChatKey, x.BotChat.TimeZoneId })
            .SingleOrDefaultAsync(cancellationToken) ?? throw new KeyNotFoundException("Кэмп не найден.");
        var rows = await dbContext.CampRegistrations.AsNoTracking().Where(x => x.CampId == campId)
            .Include(x => x.Participant).Include(x => x.SelectedDays).ToArrayAsync(cancellationToken);
        var participants = rows.Select(x => new CampAdminParticipant(
            x.ParticipantId,
            CampParticipantPresentation.RegistrationDisplayName(x.DisplayName,
                x.Participant.PreferredDisplayName, x.Participant.DisplayName)
                ?? NormalizeUsername(x.Participant.TelegramUsername) ?? "Имя не указано",
            string.IsNullOrWhiteSpace(x.City) ? null : x.City.Trim(),
            x.SelectedDays.Select(day => day.Date).Distinct().Order().ToArray(),
            x.NeedsAccommodation, NormalizeUsername(x.Participant.TelegramUsername),
            ParticipantPresentation.GetContactUrl(x.Participant)))
            .OrderBy(x => x.DisplayName, StringComparer.Create(CultureInfo.GetCultureInfo("ru-RU"), true))
            .ThenBy(x => x.ParticipantId).ToArray();
        // This endpoint returns the complete matching roster, never a page of it.
        return new(camp.Id, camp.Name, participants.Where(filter.Matches).ToArray())
        {
            CommunityKey = camp.BotChatKey, TimeZoneId = camp.TimeZoneId, TotalCount = participants.Length,
            AvailableDates = participants.SelectMany(x => x.SelectedDates).Distinct().Order().ToArray(),
            FilterDescription = filter.Describe()
        };
    }

    public async Task<CampParticipantExportFile> ExportAsync(long actorTelegramUserId, long campId,
        string format, CancellationToken cancellationToken, CampParticipantFilter? filter = null)
    {
        var roster = await GetAsync(actorTelegramUserId, campId, cancellationToken, filter);
        return CampParticipantExport.Create(roster, format, timeProvider.GetUtcNow(), cancellationToken);
    }

    public async Task<CampParticipantDmResult> SendFileToActorAsync(long actorTelegramUserId, long campId,
        string format, CancellationToken cancellationToken, CampParticipantFilter? filter = null)
    {
        var file = await ExportAsync(actorTelegramUserId, campId, format, cancellationToken, filter);
        using var stream = new MemoryStream(file.Content, writable: false);
        try
        {
            await botClient.SendDocument(actorTelegramUserId, InputFile.FromStream(stream, file.FileName),
                caption: "Участники кэмпа · " + file.ParticipantCount, cancellationToken: cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw DeliveryFailure(exception, campId, 0, 0);
        }
        return new(1, file.ParticipantCount);
    }

    public async Task<CampParticipantDmResult> SendToActorAsync(long actorTelegramUserId, long campId,
        CancellationToken cancellationToken, CampParticipantFilter? filter = null)
    {
        var roster = await GetAsync(actorTelegramUserId, campId, cancellationToken, filter);
        var chunks = CampParticipantMessages.Build(roster);
        var keyboard = new InlineKeyboardMarkup(InlineKeyboardButton.WithWebApp("Открыть список",
            new WebAppInfo { Url = links.CampParticipants(roster.CommunityKey) }));
        var sent = 0;
        var participantsSent = 0;
        var usePlainText = false;
        try
        {
            foreach (var chunk in chunks)
            {
                if (!usePlainText)
                {
                    try
                    {
                        await botClient.SendRichMessage(actorTelegramUserId, chunk.RichMessage,
                            replyMarkup: keyboard, cancellationToken: cancellationToken);
                    }
                    catch (ApiRequestException exception) when (CampParticipantMessages.IsUnsupported(exception))
                    {
                        // Only this definitively rejected chunk is sent again; delivered chunks are never replayed.
                        usePlainText = true;
                    }
                }
                if (usePlainText)
                    await botClient.SendMessage(actorTelegramUserId, chunk.Text,
                        replyMarkup: keyboard, cancellationToken: cancellationToken);
                sent++;
                participantsSent += chunk.ParticipantCount;
            }
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw DeliveryFailure(exception, campId, sent, participantsSent);
        }
        return new(sent, roster.Participants.Count);
    }

    private CampParticipantDeliveryException DeliveryFailure(Exception exception, long campId, int sent, int participantsSent)
    {
        // Avoid participant contents and raw transport exceptions, whose URLs may contain a token.
        logger.LogWarning("Camp participant delivery failed for Camp {CampId}: {ErrorType}, confirmed messages {Count}.",
            campId, exception.GetType().Name, sent);
        var prefix = sent > 0 ? $"Отправлена только часть списка: {participantsSent} участников, сообщений: {sent}. " : "";
        if (exception is ApiRequestException api && api.ErrorCode is >= 400 and < 500)
        {
            var privateChatUnavailable = api.ErrorCode == 403 || (api.ErrorCode == 400 &&
                (api.Message.Contains("chat not found", StringComparison.OrdinalIgnoreCase)
                    || api.Message.Contains("can't initiate", StringComparison.OrdinalIgnoreCase)));
            var message = privateChatUnavailable
                ? "Откройте личный чат с ботом, нажмите «Старт» и разрешите сообщения."
                : "Telegram отклонил отправку. Попробуйте скачать файл.";
            return new(sent > 0 ? "camp_participant_delivery_partial" : privateChatUnavailable
                ? "private_chat_required" : "camp_participant_delivery_failed", prefix + message
                + (sent > 0 ? " Перед повторной отправкой проверьте личный чат: уже отправленные строки повторятся." : ""), exception);
        }
        return new("camp_participant_delivery_unknown", prefix
            + "Не удалось подтвердить доставку. Проверьте личный чат с ботом перед повторной отправкой: сообщение могло дойти.", exception);
    }

    private static string? NormalizeUsername(string? value) =>
        string.IsNullOrWhiteSpace(value?.Trim().TrimStart('@')) ? null : value.Trim().TrimStart('@');
}
