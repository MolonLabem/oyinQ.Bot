using oyinQ.Bot.Features.Collections;

namespace oyinQ.Bot.Tests;

public sealed class RollMoveReconciliationTests
{
    [Fact]
    public void CandidatesNormalizeWhitespaceHtmlEntitiesAndDuplicates() =>
        Assert.Equal(new long[] { 10, 20, 437383 }, RollMoveReconciliation.ParseCandidates(" 20\n10 20\r\n437383&#x20;"));

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("12.5")]
    [InlineData("edition")]
    public void InvalidIdsAreRejected(string text) => Assert.Throws<ArgumentException>(() => RollMoveReconciliation.ParseCandidates(text));

    [Fact]
    public void ReviewIdsAreNeverOwnedOrMissingCandidatesEvenWhenProviderResolvesThem()
    {
        var result = RollMoveReconciliation.Classify([1, 2, 3, 4, 5], new HashSet<long> { 1, 2, 3, 4 },
            new HashSet<long> { 1, 3 }, new HashSet<long> { 3, 4 });
        Assert.Equal(new long[] { 1 }, result.Owned);
        Assert.Equal(new long[] { 2 }, result.Missing);
        Assert.Equal(new long[] { 3, 4 }, result.Review);
        Assert.Equal(new long[] { 5 }, result.Unresolved);
    }
}
