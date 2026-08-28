using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Games;
using oyinQ.Bot.Integrations.Telegram;

namespace oyinQ.Bot.Tests;

public sealed class UxHelperTests
{
    [Theory]
    [InlineData("https://boardgamegeek.com/boardgame/13", "🔗 Открыть BGG")]
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
