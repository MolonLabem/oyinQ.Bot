using oyinQ.Bot.Data.Entities;

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

public sealed class CampImportNotificationService(oyinQ.Bot.Features.Notifications.NotificationService notifications)
{
    public Task NotifyAsync(long telegramUserId, string communityKey, Guid importId,
        CampImportConfirmationResult result, CancellationToken cancellationToken) => result.WasAlreadyConfirmed
        ? Task.CompletedTask
        : notifications.EnqueueAsync(new(telegramUserId, NotificationKind.ImportCompleted, importId.ToString("N"),
            $"Импорт BGG завершён. Добавлено: {result.Added}. Результат и выбор копий доступны в профиле.", communityKey, ImportPublicId: importId), cancellationToken);
}
