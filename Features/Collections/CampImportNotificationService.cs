using Microsoft.Extensions.Options;
using oyinQ.Bot.Common.Options;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace oyinQ.Bot.Features.Collections;

public sealed class CampImportNotificationService(ITelegramBotClient botClient, IOptions<BotOptions> botOptions,
    ILogger<CampImportNotificationService> logger)
{
    public async Task NotifyAsync(long telegramUserId, string communityKey, Guid importId,
        CampImportConfirmationResult result, CancellationToken cancellationToken)
    {
        if (result.WasAlreadyConfirmed || result.Skipped.Count == 0) return;
        var skipped = result.Skipped.Values.Sum();
        var lines = new List<string> { "Импорт BGG завершён.", "", $"Добавлено: {result.Added}", $"Не добавлено: {skipped}", "" };
        foreach (var pair in result.Skipped.OrderBy(x => x.Key))
            lines.Add(pair.Key switch
            {
                Data.Entities.CampImportSkipReason.AlreadyInBaseCollection => $"{pair.Value} уже есть в базовой коллекции кэмпа.",
                Data.Entities.CampImportSkipReason.AlreadyAddedManually => $"{pair.Value} вы уже добавили вручную.",
                Data.Entities.CampImportSkipReason.InvalidOrUnsupportedItem => $"{pair.Value} не поддерживаются.",
                _ => $"Для {pair.Value} недостаточно данных BGG."
            });
        try
        {
            InlineKeyboardMarkup? keyboard = null;
            if (result.HasOverridableItems)
            {
                var url = $"{botOptions.Value.PublicBaseUrl.TrimEnd('/')}/app/?community={Uri.EscapeDataString(communityKey)}&tab=mine&import={importId}";
                keyboard = new InlineKeyboardMarkup([[
                    InlineKeyboardButton.WithWebApp("Выбрать действие", new WebAppInfo { Url = url })
                ]]);
            }
            await botClient.SendMessage(telegramUserId, string.Join('\n', lines), replyMarkup: keyboard,
                cancellationToken: cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Could not send Camp import summary {ImportId}.", importId);
        }
    }
}
