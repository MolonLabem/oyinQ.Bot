using Telegram.Bot.Types.ReplyMarkups;

namespace oyinQ.Bot.Integrations.Telegram;

public static class Keyboards
{
    public static ReplyKeyboardMarkup MainMenu { get; } = BuildMainMenu(includeAdmin: false);

    public static ReplyKeyboardMarkup MainMenuFor(bool includeAdmin) =>
        includeAdmin ? BuildMainMenu(includeAdmin: true) : MainMenu;

    public static InlineKeyboardMarkup RegistrationDays { get; } = new(
        new InlineKeyboardButton[][]
        {
            [
                InlineKeyboardButton.WithCallbackData("1 день", "reg:days:1"),
                InlineKeyboardButton.WithCallbackData("2 дня", "reg:days:2"),
                InlineKeyboardButton.WithCallbackData("3 дня", "reg:days:3")
            ]
        });

    public static InlineKeyboardMarkup Accommodation { get; } = new(
        new InlineKeyboardButton[][]
        {
            [
                InlineKeyboardButton.WithCallbackData("Да", "reg:accommodation:yes"),
                InlineKeyboardButton.WithCallbackData("Нет", "reg:accommodation:no")
            ]
        });

    public static InlineKeyboardMarkup DisplayName { get; } = new(
        new InlineKeyboardButton[][]
        {
            [InlineKeyboardButton.WithCallbackData("Пропустить — использовать имя Telegram", "reg:name:skip")]
        });

    public static InlineKeyboardMarkup Profile { get; } = new(
        new InlineKeyboardButton[][]
        {
            [InlineKeyboardButton.WithCallbackData("📝 Изменить регистрацию", "reg:edit")],
            [InlineKeyboardButton.WithCallbackData("🎒 Мои игры", "game:my:menu")],
            [InlineKeyboardButton.WithCallbackData("🔥 Мои хотелки", "game:mywanted:0")],
            [InlineKeyboardButton.WithCallbackData("💳 Оплата участия", "reg:payment")]
        });

    public static InlineKeyboardMarkup Payment { get; } = new(
        new InlineKeyboardButton[][]
        {
            [InlineKeyboardButton.WithCallbackData("← Назад", "reg:profile")]
        });

    private static ReplyKeyboardMarkup BuildMainMenu(bool includeAdmin)
    {
        var rows = new List<KeyboardButton[]>
        {
            new KeyboardButton[]
            {
                new("🎲 Игры"),
                new("➕ Добавить игры")
            },
            new KeyboardButton[]
            {
                new("🔥 Хочу сыграть"),
                new("▶️ Собрать игру")
            },
            new KeyboardButton[]
            {
                new("🎲 Текущие сборы"),
                new("👤 Профиль")
            }
        };

        if (includeAdmin)
        {
            rows.Add([new KeyboardButton("🛠 Админ-панель")]);
        }

        return new ReplyKeyboardMarkup(rows)
        {
            ResizeKeyboard = true,
            IsPersistent = true
        };
    }
}
