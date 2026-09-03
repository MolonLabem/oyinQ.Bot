using System.Text.Json;

namespace oyinQ.Bot.Common.Options;

public enum BotMode
{
    Club = 0,
    Camp = 1
}

public sealed record BotCommunity(
    string Key,
    string Name,
    long TelegramChatId,
    BotMode Mode,
    string TimeZoneId,
    DateOnly? StartDate = null,
    DateOnly? EndDate = null);

public sealed class CommunityOptions
{
    public IReadOnlyList<BotCommunity> Communities { get; init; } = [];

    public static CommunityOptions FromConfiguration(IConfiguration configuration)
    {
        var json = configuration["CommunityBootstrap:CommunitiesJson"]?.Trim();
        if (string.IsNullOrWhiteSpace(json))
        {
            return new CommunityOptions();
        }

        return new CommunityOptions { Communities = Parse(json) };
    }

    private static IReadOnlyList<BotCommunity> Parse(string json)
    {
        CommunityConfiguration[] values;
        try
        {
            values = JsonSerializer.Deserialize<CommunityConfiguration[]>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("CommunityBootstrap:CommunitiesJson must be a valid JSON array.", exception);
        }

        var communities = values.Select(ToCommunity).ToArray();
        if (communities.Length == 0)
        {
            throw new InvalidOperationException("CommunityBootstrap:CommunitiesJson must contain at least one community when configured.");
        }

        if (communities.Select(value => value.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count() != communities.Length)
        {
            throw new InvalidOperationException("CommunityBootstrap:CommunitiesJson contains duplicate keys.");
        }

        if (communities.Select(value => value.TelegramChatId).Distinct().Count() != communities.Length)
        {
            throw new InvalidOperationException("CommunityBootstrap:CommunitiesJson contains duplicate Telegram chat IDs.");
        }

        return communities;
    }

    public static BotCommunity CreateValidated(
        string? keyValue,
        string? name,
        long telegramChatId,
        string? modeValue,
        string? timeZoneValue)
    {
        var key = keyValue?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(key)
            || key.Length > 32
            || key.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '_' and not '-'))
        {
            throw new InvalidOperationException("Ключ сообщества должен содержать 1–32 латинские буквы, цифры, _ или -.");
        }

        var normalizedName = name?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedName) || normalizedName.Length > 160)
        {
            throw new InvalidOperationException("Название сообщества обязательно и не может быть длиннее 160 символов.");
        }

        if (telegramChatId >= 0)
        {
            throw new InvalidOperationException(
                "Telegram ID группы или супергруппы должен быть отрицательным числом.");
        }

        if (!Enum.TryParse<BotMode>(modeValue, ignoreCase: true, out var mode))
        {
            throw new InvalidOperationException($"Режим сообщества «{modeValue}» не поддерживается.");
        }

        var timeZoneId = timeZoneValue?.Trim();
        if (string.IsNullOrWhiteSpace(timeZoneId) || timeZoneId.Length > 100)
        {
            throw new InvalidOperationException("Укажите часовой пояс длиной не более 100 символов.");
        }

        _ = RequireTimeZone(timeZoneId);

        return new BotCommunity(key, normalizedName, telegramChatId, mode, timeZoneId);
    }

    public static TimeZoneInfo RequireTimeZone(string? value)
    {
        var timeZoneId = value?.Trim();
        if (string.IsNullOrWhiteSpace(timeZoneId) || timeZoneId.Length > 100)
            throw new InvalidOperationException("Укажите корректный часовой пояс.");
        try { return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId); }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new InvalidOperationException($"Часовой пояс «{timeZoneId}» не найден.", exception);
        }
    }

    private static BotCommunity ToCommunity(CommunityConfiguration value) =>
        CreateValidated(value.Key, value.Name, value.TelegramChatId, value.Mode, value.TimeZone);

    private sealed class CommunityConfiguration
    {
        public string? Key { get; init; }
        public string? Name { get; init; }
        public long TelegramChatId { get; init; }
        public string? Mode { get; init; }
        public string? TimeZone { get; init; }
    }
}
