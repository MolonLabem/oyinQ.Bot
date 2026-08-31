using oyinQ.Bot.Integrations.Telegram;

namespace oyinQ.Bot.Tests;

public sealed class TelegramBotProfileTests
{
    [Fact]
    public void PrivateCommands_HaveExpectedOrderAndDescriptions() =>
        Assert.Equal(
            [
                "start:Открыть OyinQ",
                "menu:Выбрать сообщество",
                "help:Как пользоваться OyinQ",
                "privacy:Политика конфиденциальности",
                "admin:Админ-панель"
            ],
            TelegramBotProfile.PrivateCommands.Select(x => $"{x.Command}:{x.Description}"));

    [Fact]
    public void GroupCommands_ContainOnlyOyinQ() =>
        Assert.Equal(["oyinq:Открыть OyinQ"],
            TelegramBotProfile.GroupCommands.Select(x => $"{x.Command}:{x.Description}"));

    [Fact]
    public void RegisteredGroup_UsesResolvedCommunityAndRuntimeBotUsername()
    {
        var entry = TelegramGroupEntryPresentation.Build("roll-move", "Roll-Move", false,
            "RuntimeBot");

        Assert.Contains("OyinQ · Roll-Move", entry.Text);
        Assert.Equal("Открыть OyinQ", entry.ButtonText);
        Assert.Equal("https://t.me/RuntimeBot?start=community-roll-move", entry.ButtonUrl);
    }

    [Fact]
    public void UnknownGroup_DoesNotExposeAdminControlsToRegularUser()
    {
        var entry = TelegramGroupEntryPresentation.Build(null, null, false, "RuntimeBot");

        Assert.Contains("пока не подключён", entry.Text);
        Assert.Null(entry.ButtonText);
        Assert.Null(entry.ButtonUrl);
    }

    [Fact]
    public void UnknownGroup_OffersAdminPathOnlyToGlobalAdministrator()
    {
        var entry = TelegramGroupEntryPresentation.Build(null, null, true, "RuntimeBot");

        Assert.Equal("Открыть админ-панель", entry.ButtonText);
        Assert.Equal("https://t.me/RuntimeBot?start=menu", entry.ButtonUrl);
    }

    [Fact]
    public void DeepLinks_DoNotDependOnCurrentTypoUsername()
    {
        var link = TelegramBotDeepLinks.BuildStart("@FutureCleanBot", "community-club");

        Assert.Equal("https://t.me/FutureCleanBot?start=community-club", link);
        Assert.DoesNotContain("OiynQ_bot", link, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HelpAndPrivacyCopy_AreConciseAndActionable()
    {
        Assert.Contains("Основные действия находятся в Mini App", TelegramEntryText.Help);
        Assert.Equal("Политика конфиденциальности OyinQ:", TelegramEntryText.Privacy);
    }
}
