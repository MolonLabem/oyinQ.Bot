namespace oyinQ.Bot.Features.Collections;

public static class BggTaxonomyCatalog
{
    private static readonly IReadOnlyDictionary<long, string> Categories = new Dictionary<long, string>
    {
        [1001] = "Политика", [1002] = "Карточная", [1008] = "Морская", [1009] = "Абстрактная стратегия",
        [1010] = "Фэнтези", [1011] = "Транспорт", [1013] = "Фермерство", [1015] = "Цивилизация",
        [1016] = "Научная фантастика", [1017] = "Кубики", [1019] = "Варгейм", [1020] = "Исследование",
        [1021] = "Экономическая", [1022] = "Приключения", [1023] = "Блеф", [1024] = "Ужасы",
        [1025] = "Слова", [1026] = "Переговоры", [1027] = "Викторина", [1028] = "Головоломка",
        [1029] = "Строительство города", [1030] = "Для вечеринок", [1031] = "Гонки", [1032] = "Ловкость",
        [1033] = "Мафия", [1034] = "Поезда", [1035] = "Средневековье", [1037] = "В реальном времени",
        [1038] = "Спорт", [1039] = "Дедукция", [1040] = "Убийство и расследование",
        [1041] = "Детская", [1042] = "Дополнение", [1044] = "Коллекционные компоненты",
        [1045] = "Память", [1046] = "Бои", [1047] = "Миниатюры", [1050] = "Древний мир",
        [1064] = "Кино, телевидение и радио", [1079] = "Юмор", [1081] = "Шпионы",
        [1082] = "Мифология", [1084] = "Экология", [1086] = "Развитие территорий",
        [1088] = "Промышленность", [1089] = "Животные", [1090] = "Пираты", [1093] = "По книге",
        [1094] = "Образовательная", [1097] = "Путешествия", [1101] = "По видеоигре",
        [1113] = "Исследование космоса", [1116] = "Комиксы", [2481] = "Зомби"
    };

    private static readonly IReadOnlyDictionary<long, string> Mechanics = new Dictionary<long, string>
    {
        [2001] = "Очки действий", [2002] = "Выкладывание тайлов", [2004] = "Сбор наборов",
        [2005] = "Владение акциями", [2007] = "Забрать и доставить", [2008] = "Торговля",
        [2009] = "Взятки", [2011] = "Модульное поле", [2012] = "Аукцион и ставки",
        [2014] = "Ставки и блеф", [2015] = "Разные способности игроков", [2016] = "Скрытое размещение отрядов",
        [2017] = "Голосование", [2019] = "Командная игра", [2020] = "Одновременный выбор действий",
        [2023] = "Совместная игра", [2026] = "Шестиугольная сетка", [2027] = "Повествование",
        [2028] = "Ролевая игра", [2035] = "Брось и ходи", [2040] = "Управление рукой",
        [2041] = "Открытый драфт", [2046] = "Перемещение по областям", [2047] = "Память",
        [2048] = "Построение узоров", [2060] = "Распознавание образов", [2072] = "Броски кубиков",
        [2073] = "Актёрская игра", [2078] = "Перемещение по точкам", [2080] = "Влияние на территории",
        [2081] = "Построение сети и маршрутов", [2082] = "Размещение рабочих", [2661] = "Испытай удачу",
        [2664] = "Составление колоды, мешка или пула", [2676] = "Перемещение по сетке",
        [2813] = "Рондель", [2814] = "Предатель", [2819] = "Одиночная игра",
        [2822] = "Сценарии и кампании", [2824] = "Наследуемая игра", [2831] = "В реальном времени",
        [2838] = "Драфт действий", [2849] = "Дерево технологий", [2891] = "Скрытые роли",
        [2893] = "Ограниченное общение", [2897] = "Изменяемая подготовка", [2900] = "Рынок",
        [2915] = "Переговоры", [2920] = "Закрытый аукцион", [2940] = "Квадратная сетка",
        [2967] = "Скрытое перемещение", [2984] = "Закрытый драфт", [3002] = "Дедукция",
        [3004] = "Конструирование колоды", [3099] = "Многофункциональные карты"
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
