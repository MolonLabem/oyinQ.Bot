using oyinQ.Bot.Integrations.Telegram;

namespace oyinQ.Bot.Tests;

public sealed class TelegramCommunityOnboardingTests
{
    [Fact]
    public async Task SuccessfulDelivery_IsAttemptedExactlyOnce()
    {
        var calls = 0;

        var result = await TelegramCommunityOnboardingDelivery.TrySendAsync(_ =>
        {
            calls++;
            return Task.CompletedTask;
        }, CancellationToken.None);

        Assert.True(result.Sent);
        Assert.Null(result.Warning);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task FailedDelivery_ReturnsWarningWithoutThrowingOrRetrying()
    {
        var calls = 0;

        var result = await TelegramCommunityOnboardingDelivery.TrySendAsync(_ =>
        {
            calls++;
            throw new InvalidOperationException("Telegram unavailable");
        }, CancellationToken.None);

        Assert.False(result.Sent);
        Assert.Contains("Сообщество создано", result.Warning);
        Assert.Equal(1, calls);
    }
}
