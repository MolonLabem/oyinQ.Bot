using Telegram.Bot.Types.ReplyMarkups;

namespace oyinQ.Bot.Integrations.Telegram;

public static class Keyboards
{
    public static ReplyKeyboardMarkup MainMenu { get; } = new(
        new KeyboardButton[][]
        {
            ["🎲 Игры", "➕ Добавить игры"],
            ["🔥 Хочу сыграть", "▶️ Собрать игру"],
            ["👤 Моё"]
        })
    {
        ResizeKeyboard = true,
        IsPersistent = true
    };

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

    public static InlineKeyboardMarkup Profile { get; } = new(
        new InlineKeyboardButton[][]
        {
            [InlineKeyboardButton.WithCallbackData("Изменить регистрацию", "reg:edit")],
            [InlineKeyboardButton.WithCallbackData("Мои игры", "reg:mygames")],
            [InlineKeyboardButton.WithCallbackData("Мои хотелки", "reg:wishlist")]
        });
}
