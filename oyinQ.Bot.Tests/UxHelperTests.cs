using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Games;
using oyinQ.Bot.Integrations.Telegram;
using oyinQ.Bot.Integrations.Tesera;

namespace oyinQ.Bot.Tests;

public sealed class UxHelperTests
{
    [Theory]
    [InlineData("https://tesera.ru/game/ark-nova", "ark-nova")]
    [InlineData("https://www.tesera.ru/game/%D0%B8%D0%B3%D1%80%D0%B0/", "игра")]
    [InlineData("tesera.ru/game/catan?foo=bar", "catan")]
    public void TeseraGameUrlParser_ParsesGameAlias(string value, string expected)
    {
        Assert.Equal(expected, TeseraGameUrlParser.Parse(value));
    }

    [Theory]
    [InlineData("https://tesera.ru/user/sardar")]
    [InlineData("https://example.com/game/catan")]
    [InlineData("catan")]
    public void TeseraGameUrlParser_RejectsNonGameLinks(string value)
    {
        Assert.Null(TeseraGameUrlParser.Parse(value));
    }

    [Theory]
    [InlineData("https://boardgamegeek.com/boardgame/13", "🔗 Открыть BGG")]
    [InlineData("https://tesera.ru/game/catan", "🔗 Открыть Tesera")]
    [InlineData("https://example.com/game/1", "🔗 Открыть страницу игры")]
    public void GameExternalLinkLabel_IsSourceAware(string url, string expected)
    {
        Assert.Equal(expected, GameExternalLinkLabel.ForUrl(url));
    }

    [Fact]
    public void ParticipantPresentation_PrefersConfiguredName_AndBuildsTelegramLink()
    {
        var participant = new Participant
        {
            TelegramUserId = 123456,
            DisplayName = "Telegram Name",
            PreferredDisplayName = "BoardCamp Name"
        };

        Assert.Equal("BoardCamp Name", ParticipantPresentation.GetDisplayName(participant));
        Assert.Equal(
            "<a href=\"tg://user?id=123456\">BoardCamp Name</a>",
            ParticipantPresentation.ToHtmlLink(participant));
    }

    [Fact]
    public void MainMenuFor_ShowsProfileLabel_AndAdminPanelOnlyForAdmins()
    {
        var regularButtons = Keyboards.MainMenuFor(includeAdmin: false)
            .Keyboard
            .SelectMany(row => row)
            .Select(button => button.Text)
            .ToArray();
        var adminButtons = Keyboards.MainMenuFor(includeAdmin: true)
            .Keyboard
            .SelectMany(row => row)
            .Select(button => button.Text)
            .ToArray();

        Assert.Contains("👤 Профиль", regularButtons);
        Assert.DoesNotContain("👤 Моё", regularButtons);
        Assert.DoesNotContain("🛠 Админ", regularButtons);
        Assert.Contains("🛠 Админ", adminButtons);
    }
}
