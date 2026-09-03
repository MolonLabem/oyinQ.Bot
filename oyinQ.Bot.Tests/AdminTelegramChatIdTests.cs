using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data.Entities;

namespace oyinQ.Bot.Tests;

public sealed class AdminTelegramChatIdTests
{
    [Fact]
    public void SelectedCommunities_RetainExactBotApiChatIds()
    {
        var groupA = new OyinQCommunity { Key = "a", Mode = BotMode.Club, TelegramChatId = -1001234567890 };
        var groupB = new OyinQCommunity { Key = "b", Mode = BotMode.Camp, TelegramChatId = -1009876543210 };

        Assert.Equal(-1001234567890, groupA.TelegramChatId);
        Assert.Equal(-1009876543210, groupB.TelegramChatId);
        Assert.NotEqual(groupA.TelegramChatId, groupB.TelegramChatId);
    }
}
