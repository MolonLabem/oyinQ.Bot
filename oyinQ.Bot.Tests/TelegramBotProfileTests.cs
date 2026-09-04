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
        Assert.Equal(["oiynq:Открыть OyinQ"],
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
    public void MainMiniAppDeepLink_OpensAppWithoutStartCommand()
    {
        var link = TelegramBotDeepLinks.BuildMainMiniApp("@RuntimeBot", "g-payload-club");

        Assert.Equal("https://t.me/RuntimeBot?startapp=g-payload-club", link);
        Assert.DoesNotContain("?start=", link, StringComparison.Ordinal);
    }

    [Fact]
    public void HelpAndPrivacyCopy_AreConciseAndActionable()
    {
        Assert.Equal(TelegramEntryText.FunctionalityGuide, TelegramEntryText.Help);
        Assert.DoesNotContain("Mini App", TelegramEntryText.Help);
        Assert.Contains("Профиль", TelegramEntryText.Help);
        Assert.Contains("напоминания", TelegramBotProfile.Description);
        Assert.Equal("Политика конфиденциальности OyinQ:", TelegramEntryText.Privacy);
    }
    [Fact]
    public void StartAndHelpShareGuide_MenuStaysFocused_AndContextualDestinationSurvives()
    {
        Assert.Equal(TelegramEntryText.ForPrivateCommand("/start"), TelegramEntryText.ForPrivateCommand("/help"));
        Assert.Null(TelegramEntryText.ForPrivateCommand("/menu"));
        var links = new MiniAppLinkBuilder(Microsoft.Extensions.Options.Options.Create(new oyinQ.Bot.Common.Options.BotOptions { PublicBaseUrl = "https://test.example" }));
        var id = Guid.NewGuid();
        var gathering = MiniAppStartParameter.Parse("/start " + MiniAppStartParameter.ForGathering("club", id));
        Assert.Equal(links.Gathering("club", id), links.FromStartContext(gathering!));
        var import = MiniAppStartParameter.Parse("/start " + MiniAppStartParameter.ForCampImport("camp", id));
        Assert.Equal(links.CampImport("camp", id), links.FromStartContext(import!));
        Assert.Equal(links.Community("club"), links.FromStartContext(MiniAppStartParameter.Parse("/start community-club")!));
    }
}
