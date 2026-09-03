using Telegram.Bot.Types;

namespace oyinQ.Bot.Integrations.Telegram;

public static class TelegramBotProfile
{
    public const string ShortDescription = "Настольные игры, сборы и коллекции клубов и кэмпов.";
    public const string Description =
        "OyinQ помогает находить настольные игры, создавать сборы и присоединяться к ним. " +
        "Клубы ведут общую коллекцию, а участники кэмпов отмечают игры, которые привезут. " +
        "Все основные действия доступны в удобном Mini App.";

    public static IReadOnlyList<BotCommand> PrivateCommands { get; } =
    [
        new() { Command = "start", Description = "Открыть OyinQ" },
        new() { Command = "menu", Description = "Выбрать сообщество" },
        new() { Command = "help", Description = "Как пользоваться OyinQ" },
        new() { Command = "privacy", Description = "Политика конфиденциальности" },
        new() { Command = "admin", Description = "Админ-панель" }
    ];

    public static IReadOnlyList<BotCommand> GroupCommands { get; } =
    [
        new() { Command = "oyinq", Description = "Открыть OyinQ" }
    ];
}

public static class TelegramBotDeepLinks
{
    public static string BuildStart(string runtimeUsername, string startParameter)
    {
        var username = NormalizeUsername(runtimeUsername);
        if (string.IsNullOrWhiteSpace(startParameter))
            throw new ArgumentException("Параметр запуска Telegram обязателен.", nameof(startParameter));
        return $"https://t.me/{username}?start={Uri.EscapeDataString(startParameter)}";
    }

    public static string BuildMainMiniApp(string runtimeUsername, string startParameter)
    {
        var username = NormalizeUsername(runtimeUsername);
        if (string.IsNullOrWhiteSpace(startParameter))
            throw new ArgumentException("Параметр запуска Telegram обязателен.", nameof(startParameter));
        return $"https://t.me/{username}?startapp={Uri.EscapeDataString(startParameter)}";
    }

    private static string NormalizeUsername(string runtimeUsername)
    {
        var username = runtimeUsername?.Trim().TrimStart('@');
        if (string.IsNullOrWhiteSpace(username))
            throw new ArgumentException("У Telegram-бота отсутствует username.", nameof(runtimeUsername));
        return username;
    }
}

public static class TelegramEntryText
{
    public static string GroupConnected(string communityName) =>
        $"🎲 OyinQ · {communityName}\n\nСборы, игры и коллекция этого сообщества.";

    public const string GroupUnknown =
        "OyinQ пока не подключён к этой группе.\n\nАдминистратор OyinQ может добавить её через админ-панель.";

    public const string Help =
        "OyinQ помогает клубам и кэмпам организовывать настольные игры.\n\n" +
        "🎲 Сборы\nСоздавайте партии и записывайтесь на игры.\n\n" +
        "📚 Игры\nСмотрите доступную коллекцию и находите игры.\n\n" +
        "🧳 Кэмпы\nРегистрируйтесь и отмечайте игры, которые сможете привезти.\n\n" +
        "Основные действия находятся в Mini App.";

    public const string Privacy = "Политика конфиденциальности OyinQ:";

    public const string CommunityOnboarding =
        "🎲 OyinQ подключён к этой группе.\n\n" +
        "Для игр и сборов используйте /oyinq.\n" +
        "Объявления о сборах будут появляться здесь автоматически.";
}

public sealed record TelegramGroupEntry(string Text, string? ButtonText, string? ButtonUrl);

public static class TelegramGroupEntryPresentation
{
    public static TelegramGroupEntry Build(string? communityKey, string? communityName,
        bool isAdministrator, string? runtimeUsername)
    {
        if (string.IsNullOrWhiteSpace(communityKey))
        {
            var adminUrl = isAdministrator && !string.IsNullOrWhiteSpace(runtimeUsername)
                ? TelegramBotDeepLinks.BuildStart(runtimeUsername, "menu")
                : null;
            return new(TelegramEntryText.GroupUnknown,
                adminUrl is null ? null : "Открыть админ-панель", adminUrl);
        }

        var url = string.IsNullOrWhiteSpace(runtimeUsername)
            ? null
            : TelegramBotDeepLinks.BuildStart(runtimeUsername,
                MiniAppStartParameter.ForCommunity(communityKey));
        var text = TelegramEntryText.GroupConnected(communityName ?? communityKey);
        if (url is null) text += "\n\nОткройте личный чат с ботом, чтобы продолжить.";
        return new(text, url is null ? null : "Открыть OyinQ", url);
    }
}
