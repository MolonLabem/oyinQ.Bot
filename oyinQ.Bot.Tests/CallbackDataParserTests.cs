using oyinQ.Bot.Integrations.Telegram;

namespace oyinQ.Bot.Tests;

public sealed class CallbackDataParserTests
{
    [Theory]
    [InlineData("game", "show", 12)]
    [InlineData("interest", "toggle", 34)]
    [InlineData("copy", "bring", 56)]
    [InlineData("session", "join", 78)]
    [InlineData("reg", "days", 2)]
    [InlineData("admin", "participants", 1)]
    public void BuildAndParse_RoundTrips(string prefix, string action, long entityId)
    {
        var value = CallbackData.Build(prefix, action, entityId);

        var parsed = CallbackData.TryParse(value, out var result);

        Assert.True(parsed);
        Assert.Equal(prefix, result.Prefix);
        Assert.Equal(action, result.Action);
        Assert.Equal(entityId, result.EntityId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("game")]
    [InlineData("game:show")]
    [InlineData("game:show:not-a-number")]
    [InlineData("game:show:0")]
    [InlineData("game:show:-1")]
    [InlineData("game:show:1:extra")]
    public void TryParse_InvalidPayload_ReturnsFalse(string? value)
    {
        Assert.False(CallbackData.TryParse(value, out _));
    }
}
