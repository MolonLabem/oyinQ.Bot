using System.Globalization;
using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data;
using oyinQ.Bot.Features.MiniApp;
using oyinQ.Bot.Integrations.Telegram;
using Telegram.Bot;

namespace oyinQ.Bot.Features.Admin;

public sealed record CampAdminParticipant(
    long ParticipantId,
    string DisplayName,
    string? City,
    IReadOnlyList<DateOnly> SelectedDates,
    bool NeedsAccommodation,
    string? TelegramUsername,
    string? ContactUrl);

public sealed record CampAdminParticipants(
    long CampId,
    string CampName,
    IReadOnlyList<CampAdminParticipant> Participants);

public sealed record CampParticipantDmResult(int MessageCount, int ParticipantCount);

public sealed class CampParticipantAdminService(
    AppDbContext dbContext,
    IAdminAuthorizationService authorization,
    ITelegramBotClient botClient,
    ILogger<CampParticipantAdminService> logger)
{
    private const int TelegramMessageLimit = 3900;

    public async Task<CampAdminParticipants> GetAsync(long actorTelegramUserId, long campId,
        CancellationToken cancellationToken)
    {
        await EnsureAuthorizedAsync(actorTelegramUserId, campId, cancellationToken);
        var camp = await dbContext.Camps.AsNoTracking()
            .Where(x => x.Id == campId)
            .Select(x => new { x.Id, x.Name })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("Кэмп не найден.");
        var rows = await dbContext.CampRegistrations.AsNoTracking()
            .Where(x => x.CampId == campId)
            .Include(x => x.Participant)
            .Include(x => x.SelectedDays)
            .OrderBy(x => x.DisplayName ?? x.Participant.PreferredDisplayName ?? x.Participant.DisplayName)
            .ThenBy(x => x.ParticipantId)
            .ToArrayAsync(cancellationToken);
        var participants = rows.Select(x => new CampAdminParticipant(
            x.ParticipantId,
            CampParticipantPresentation.RegistrationDisplayName(x.DisplayName,
                x.Participant.PreferredDisplayName, x.Participant.DisplayName)
                ?? x.Participant.TelegramUsername ?? x.Participant.TelegramUserId.ToString(CultureInfo.InvariantCulture),
            x.City,
            x.SelectedDays.Select(day => day.Date).Order().ToArray(),
            x.NeedsAccommodation == true,
            NormalizeUsername(x.Participant.TelegramUsername),
            ParticipantPresentation.GetContactUrl(x.Participant)))
            .ToArray();
        return new(camp.Id, camp.Name, participants);
    }

    public async Task<CampParticipantDmResult> SendToActorAsync(long actorTelegramUserId, long campId,
        CancellationToken cancellationToken)
    {
        var value = await GetAsync(actorTelegramUserId, campId, cancellationToken);
        var messages = BuildMessages(value);
        try
        {
            foreach (var message in messages)
                await botClient.SendMessage(actorTelegramUserId, message, cancellationToken: cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception,
                "Could not send Camp {CampId} participant export to administrator {TelegramUserId}.",
                campId, actorTelegramUserId);
            throw new InvalidOperationException(
                "Не удалось отправить список в личный чат. Сначала откройте диалог с ботом и попробуйте ещё раз.");
        }
        return new(messages.Count, value.Participants.Count);
    }

    public static IReadOnlyList<string> BuildMessages(CampAdminParticipants value)
    {
        var header = $"⛺ {value.CampName}\nЗарегистрировано: {value.Participants.Count}";
        if (value.Participants.Count == 0) return [$"{header}\n\nПока никто не зарегистрировался."];
        var text = new System.Text.StringBuilder(header);
        for (var index = 0; index < value.Participants.Count; index++)
        {
            var participant = value.Participants[index];
            var telegram = participant.TelegramUsername is not null
                ? $"\nTelegram: @{participant.TelegramUsername}"
                : participant.ContactUrl is not null ? $"\nTelegram: {participant.ContactUrl}" : string.Empty;
            var dates = participant.SelectedDates.Count == 0
                ? "не указаны"
                : string.Join(", ", participant.SelectedDates.Select(x => x.ToString("dd.MM", CultureInfo.InvariantCulture)));
            var block = $"{index + 1}. {participant.DisplayName}{telegram}\nГород: {participant.City ?? "не указан"}\nДаты: {dates}\nЖильё: {(participant.NeedsAccommodation ? "нужно" : "не нужно")}";
            text.Append("\n\n").Append(block);
        }
        return SplitMessages(text.ToString());
    }

    private static IReadOnlyList<string> SplitMessages(string text)
    {
        var messages = new List<string>();
        while (text.Length > TelegramMessageLimit)
        {
            var splitAt = text.LastIndexOf('\n', TelegramMessageLimit - 1, TelegramMessageLimit);
            if (splitAt < TelegramMessageLimit / 2) splitAt = TelegramMessageLimit;
            messages.Add(text[..splitAt].TrimEnd());
            text = text[splitAt..].TrimStart('\n');
        }
        if (text.Length > 0) messages.Add(text);
        return messages;
    }

    private async Task EnsureAuthorizedAsync(long actorTelegramUserId, long campId,
        CancellationToken cancellationToken)
    {
        if (!await authorization.CanAdministerCampAsync(actorTelegramUserId, campId, cancellationToken))
            throw new UnauthorizedAccessException("Нет доступа к участникам этого кэмпа.");
    }

    private static string? NormalizeUsername(string? value)
    {
        var username = value?.Trim().TrimStart('@');
        return string.IsNullOrWhiteSpace(username) ? null : username;
    }
}
