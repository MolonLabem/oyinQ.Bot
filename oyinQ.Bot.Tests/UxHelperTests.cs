using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Integrations.Telegram;

namespace oyinQ.Bot.Tests;

public sealed class UxHelperTests
{
    [Fact]
    public void ParticipantPresentation_PrefersConfiguredName_AndBuildsTelegramLink()
    {
        var participant = new Participant
        {
            TelegramUserId = 123456,
            DisplayName = "Telegram Name",
            PreferredDisplayName = "Camp Name"
        };

        Assert.Equal("Camp Name", ParticipantPresentation.GetDisplayName(participant));
        Assert.Equal(
            "<a href=\"tg://user?id=123456\">Camp Name</a>",
            ParticipantPresentation.ToHtmlLink(participant));
    }

}
