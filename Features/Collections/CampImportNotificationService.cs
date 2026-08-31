using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Integrations.Telegram;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace oyinQ.Bot.Features.Collections;

public static class CampImportCallbackData
{
    private const string Prefix = "campimp:";

    public static string Create(Guid importId, CampImportOverrideResolution resolution) =>
        $"{Prefix}{importId:N}:{(resolution == CampImportOverrideResolution.AddPersonalCopies ? "add" : "keep")}";

    public static bool TryParse(string? value, out Guid importId, out CampImportOverrideResolution resolution)
    {
        importId = default;
        resolution = default;
        if (value is null || !value.StartsWith(Prefix, StringComparison.Ordinal)) return false;
        var parts = value[Prefix.Length..].Split(':');
        if (parts.Length != 2 || !Guid.TryParseExact(parts[0], "N", out importId)) return false;
        resolution = parts[1] switch
        {
            "add" => CampImportOverrideResolution.AddPersonalCopies,
            "keep" => CampImportOverrideResolution.KeepBaseCollection,
            _ => (CampImportOverrideResolution)(-1)
        };
        return Enum.IsDefined(resolution);
    }
}

public sealed class CampImportNotificationService(ITelegramBotClient botClient, MiniAppLinkBuilder links,
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
                Data.Entities.CampImportSkipReason.AlreadyInBaseCollection => $"{pair.Value} уже есть в общей коллекции кэмпа.",
                Data.Entities.CampImportSkipReason.AlreadyAddedManually => $"{pair.Value} вы уже добавили вручную.",
                Data.Entities.CampImportSkipReason.InvalidOrUnsupportedItem => $"{pair.Value} не поддерживаются.",
                _ => $"Для {pair.Value} недостаточно данных BGG."
            });
        try
        {
            InlineKeyboardMarkup? keyboard = null;
            if (result.HasOverridableItems)
            {
                var url = links.CampImport(communityKey, importId);
                keyboard = new InlineKeyboardMarkup([
                    [InlineKeyboardButton.WithCallbackData("Оставить как есть",
                        CampImportCallbackData.Create(importId, Data.Entities.CampImportOverrideResolution.KeepBaseCollection))],
                    [InlineKeyboardButton.WithCallbackData("Добавить мои копии",
                        CampImportCallbackData.Create(importId, Data.Entities.CampImportOverrideResolution.AddPersonalCopies))],
                    [InlineKeyboardButton.WithWebApp("Выбрать игры", new WebAppInfo { Url = url })]
                ]);
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
