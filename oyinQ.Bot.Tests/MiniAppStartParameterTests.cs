using oyinQ.Bot.Integrations.Telegram;

namespace oyinQ.Bot.Tests;

public sealed class MiniAppStartParameterTests
{
    [Fact]
    public void GatheringParameter_RoundTripsWithinTelegramLimit()
    {
        var id = Guid.Parse("1b67be1d-caba-41d0-855d-04bc29bd042d");
        var parameter = MiniAppStartParameter.ForGathering("club-main", id);

        var parsed = MiniAppStartParameter.Parse($"/start {parameter}");

        Assert.Equal("g-Hb5nG7rK0EGFXQS8Kb0ELQ-club-main", parameter);
        Assert.True(parameter.Length <= 64);
        Assert.Equal("club-main", parsed?.CommunityKey);
        Assert.Equal(id, parsed?.GatheringPublicId);
    }

    [Fact]
    public void CommunityParameter_ParsesWithoutGathering()
    {
        var parsed = MiniAppStartParameter.Parse("/start community-club");

        Assert.Equal("club", parsed?.CommunityKey);
        Assert.Null(parsed?.GatheringPublicId);
    }

    [Fact]
    public void CollectionGameParameter_RoundTripsCanonicalContext()
    {
        var parameter = MiniAppStartParameter.ForCollectionGame("club-main", 167791);

        var parsed = MiniAppStartParameter.Parse($"/start {parameter}");

        Assert.True(parameter.Length <= 64);
        Assert.Equal("club-main", parsed?.CommunityKey);
        Assert.Equal(167791, parsed?.CollectionBggId);
        Assert.Null(parsed?.GatheringPublicId);
    }

    [Theory]
    [InlineData("/start c-0-club")]
    [InlineData("/start c-title-club")]
    [InlineData("/start c-42-")]
    public void InvalidCollectionGameParameter_IsRejected(string text) =>
        Assert.Null(MiniAppStartParameter.Parse(text));
}
