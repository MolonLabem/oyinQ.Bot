using oyinQ.Bot.Integrations.BoardGameGeek;

namespace oyinQ.Bot.Tests;

public sealed class BggGameUrlTests
{
    [Fact]
    public void FromId_BuildsCanonicalUrlFromPositiveId() =>
        Assert.Equal("https://boardgamegeek.com/boardgame/167791", BggGameUrl.FromId(167791));

    [Theory]
    [InlineData(null)]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void FromId_OmitsMissingOrInvalidId(long? bggId) =>
        Assert.Null(BggGameUrl.FromId(bggId));
}
