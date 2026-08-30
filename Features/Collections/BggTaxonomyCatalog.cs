namespace oyinQ.Bot.Features.Collections;

public static class BggTaxonomyCatalog
{
    private static readonly IReadOnlyDictionary<long, string> Categories = new Dictionary<long, string>
    {
        [1002] = "Карточная", [1010] = "Фэнтези", [1017] = "Кубики", [1021] = "Экономическая",
        [1023] = "Блеф", [1028] = "Головоломка", [1039] = "Дедукция", [1046] = "Бои",
        [1082] = "Мифология", [1086] = "Территории", [1090] = "Города", [1115] = "Детективная"
    };

    private static readonly IReadOnlyDictionary<long, string> Mechanics = new Dictionary<long, string>
    {
        [2001] = "Скрытые роли", [2007] = "Ставки", [2011] = "Модульное поле", [2015] = "Перемещение по точкам",
        [2026] = "Закрытый аукцион", [2040] = "Управление рукой", [2072] = "Броски кубиков",
        [2080] = "Контроль территорий", [2093] = "Совместная игра", [2664] = "Составление колоды"
    };

    private static readonly IReadOnlyDictionary<long, GameType> SubdomainTypes = new Dictionary<long, GameType>
    {
        [5496] = GameType.Thematic, [5497] = GameType.Strategy, [5498] = GameType.Party,
        [5499] = GameType.Family, [4664] = GameType.War, [4665] = GameType.Children,
        [4666] = GameType.Abstract, [4667] = GameType.Customizable
    };

    // When BGG assigns several subdomains, the first mapped value in this explicit
    // product priority becomes the browse type; all source subdomains remain stored.
    private static readonly GameType[] Priority =
    [
        GameType.Children, GameType.Party, GameType.Family, GameType.Abstract,
        GameType.Strategy, GameType.Thematic, GameType.War, GameType.Customizable
    ];

    public static string LocalizeCategory(GameTaxonomyItem item) => Categories.GetValueOrDefault(item.BggId, item.Name);
    public static string LocalizeMechanic(GameTaxonomyItem item) => Mechanics.GetValueOrDefault(item.BggId, item.Name);

    public static GameType MapGameType(IReadOnlyCollection<GameTaxonomyItem> subdomains)
    {
        var mapped = subdomains.Select(item => SubdomainTypes.GetValueOrDefault(item.BggId, GameType.Other)).ToHashSet();
        return Priority.FirstOrDefault(mapped.Contains, GameType.Other);
    }

    public static string DisplayName(GameType type) => type switch
    {
        GameType.Strategy => "Стратегия", GameType.Family => "Семейная", GameType.Party => "Пати",
        GameType.Thematic => "Тематическая", GameType.Abstract => "Абстрактная", GameType.War => "Варгейм",
        GameType.Children => "Детская", GameType.Customizable => "Коллекционная", _ => "Другое"
    };
}
