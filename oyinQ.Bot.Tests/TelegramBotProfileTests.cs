using oyinQ.Bot.Integrations.Telegram;
using System.Xml.Linq;
using Telegram.Bot.Types.Enums;

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
                "privacy:О ваших данных",
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
        Assert.InRange(TelegramEntryText.Start.Length, 1, 550);
        Assert.EndsWith("Нужна инструкция? /help", TelegramEntryText.Start);
        Assert.Contains("Сборы", TelegramEntryText.Start);
        Assert.Contains("Игры", TelegramEntryText.Start);
        Assert.Contains("Профиль", TelegramEntryText.Start);
        Assert.DoesNotContain("Mini App", TelegramEntryText.Help);
        Assert.Contains("Профиль", TelegramEntryText.Help);
        Assert.Contains("напоминания", TelegramBotProfile.Description);
        Assert.Contains("данные", TelegramEntryText.Privacy);
    }
    [Fact]
    public void CommandsUseExplicitFormatting_MenuStaysFocused_AndContextualDestinationSurvives()
    {
        Assert.Equal(new(TelegramEntryText.Start, ParseMode.None), TelegramEntryText.ForPrivateCommand("/start"));
        Assert.Equal(new(TelegramEntryText.Help, ParseMode.Html), TelegramEntryText.ForPrivateCommand("/help"));
        Assert.Null(TelegramEntryText.ForPrivateCommand("/menu"));
        var links = new MiniAppLinkBuilder(Microsoft.Extensions.Options.Options.Create(new oyinQ.Bot.Common.Options.BotOptions { PublicBaseUrl = "https://test.example" }));
        var id = Guid.NewGuid();
        var gathering = MiniAppStartParameter.Parse("/start " + MiniAppStartParameter.ForGathering("club", id));
        Assert.Equal(links.Gathering("club", id), links.FromStartContext(gathering!));
        var import = MiniAppStartParameter.Parse("/start i-Hb5nG7rK0EGFXQS8Kb0ELQ-camp");
        Assert.Equal(links.CampImport("camp", Guid.Parse("1b67be1d-caba-41d0-855d-04bc29bd042d")), links.FromStartContext(import!));
        Assert.Equal(links.Community("club"), links.FromStartContext(MiniAppStartParameter.Parse("/start community-club")!));
    }

    [Fact]
    public void Help_IsOneMessageWithSixExpandableTasksAndRoomToGrow()
    {
        var document = ParseHelp(TelegramEntryText.Help);
        Assert.InRange(document.Value.Length, 1, TelegramEntryText.HelpVisibleBudget);
        var quotes = document.Elements("blockquote").ToArray();
        Assert.Equal(6, quotes.Length);
        Assert.Equal(6, document.Elements("b").Count());
        Assert.All(quotes, quote => { Assert.NotNull(quote.Attribute("expandable")); Assert.Empty(quote.Elements()); });
        Assert.All(document.Elements(), element => Assert.Contains(element.Name.LocalName, new[] { "b", "blockquote" }));
        foreach (var topic in new[] { "создать сбор", "дождаться места", "свою коллекцию", "Хотелки", "кэмпе", "после игры" })
            Assert.Contains(document.Elements("b"), heading => heading.Value.Contains(topic));
    }

    [Fact]
    public void HelpRenderer_EscapesAllTextBeforeAddingMarkup()
    {
        const string untrusted = "Имя <b> & \"название\" </blockquote>";
        var document = ParseHelp(TelegramEntryText.RenderHelp(untrusted, [new(untrusted, untrusted)]));
        Assert.Equal(untrusted, document.Element("b")!.Value);
        Assert.Equal(untrusted, document.Element("blockquote")!.Value);
        Assert.Equal(2, document.Elements().Count());
    }

    private static XElement ParseHelp(string html) => XElement.Parse("<root>" +
        html.Replace("<blockquote expandable>", "<blockquote expandable=\"true\">") + "</root>");
}
