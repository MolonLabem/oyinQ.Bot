using Microsoft.Extensions.Options;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Integrations.Telegram;

namespace oyinQ.Bot.Tests;

public sealed class UxHelperTests
{
    [Fact]
    public void MiniAppLinks_UseOneEscapedCanonicalBase()
    {
        var links = new MiniAppLinkBuilder(Options.Create(new BotOptions
            { PublicBaseUrl = "https://example.test/" }));
        var gatheringId = Guid.Parse("8d8c273c-5726-45ad-a902-c8fa022c3265");

        Assert.Equal("https://example.test/app/", links.App());
        Assert.Equal("https://example.test/app/?admin=1", links.Admin());
        Assert.Equal("https://example.test/app/?community=camp%20one&gathering=8d8c273c-5726-45ad-a902-c8fa022c3265",
            links.Gathering("camp one", gatheringId));
        Assert.Equal("https://example.test/app/?community=camp%20one&tab=games&game=167791",
            links.CollectionGame("camp one", 167791));
    }

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
        Assert.Equal("tg://user?id=123456", ParticipantPresentation.GetContactUrl(participant));
        participant.TelegramUsername = "test_user";
        Assert.Equal("https://t.me/test_user?profile", ParticipantPresentation.GetContactUrl(participant));
    }

}
