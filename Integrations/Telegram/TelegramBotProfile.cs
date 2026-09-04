using Telegram.Bot.Types;

namespace oyinQ.Bot.Integrations.Telegram;

public static class TelegramBotProfile
{
    public const string ShortDescription = "Настольные игры, сборы и коллекции клубов и кэмпов.";
    public const string Description =
        "OyinQ помогает настольным клубам и кэмпам организовывать игры: находить настолки, " +
        "вести личные и клубные коллекции, создавать сборы, записываться и вставать в лист ожидания, " +
        "договариваться, кто привезёт коробку, получать напоминания и вести историю сыгранных партий.";

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
        new() { Command = "oiynq", Description = "Открыть OyinQ" }
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

    public const string FunctionalityGuide =
        "🎲 OyinQ\n\nПомогает договориться, во что играем, кто участвует и кто принесёт коробку.\n\n" +
        "🎲 Сборы\nСмотрите ближайшие партии, записывайтесь или выходите. Если мест нет — вставайте в лист ожидания. " +
        "Создавайте свои сборы: выбирайте игру и дополнения, число игроков, описание и готовность объяснить правила.\n\n" +
        "📚 Игры\nИщите в коллекции клуба, среди коробок участников кэмпа, в своей коллекции и через BoardGameGeek. " +
        "Смотрите число игроков, категории, дополнения и кто может или точно привезёт игру.\n\n" +
        "👤 Профиль\nМоя коллекция сохраняется между клубами и кэмпами. Календарь собирает ваши предстоящие сборы из разных сообществ. " +
        "В настройках — имя и уведомления. Напоминания о приближении игры можно включить; важные изменения придут автоматически.\n\n" +
        "🏕 Кэмпы\nСначала зарегистрируйтесь и выберите дни участия. Затем создавайте сборы, записывайтесь и отмечайте, какие игры можете или точно привезёте.\n\n" +
        "📊 После партии\nОрганизатор подтверждает, состоялась ли игра, и уточняет состав. Только сыгранные партии попадают в историю. " +
        "Игроки могут открыть партию в BG Stats и поделиться ссылкой на свою запись.\n\nОткройте OyinQ кнопкой ниже.";
    public const string Help = FunctionalityGuide;
    public static string? ForPrivateCommand(string command) => command is "/start" or "/help" ? FunctionalityGuide : null;

    public const string Privacy = "Политика конфиденциальности OyinQ:";

    public const string CommunityOnboarding =
        "🎲 OyinQ подключён к этой группе.\n\n" +
        "Для игр и сборов используйте /oiynq.\n" +
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
