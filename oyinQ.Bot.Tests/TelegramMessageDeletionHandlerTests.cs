using Microsoft.Extensions.Logging.Abstractions;
using oyinQ.Bot.Features.Gatherings;
using oyinQ.Bot.Integrations.Telegram;
using Telegram.Bot.Exceptions;

namespace oyinQ.Bot.Tests;

public sealed class TelegramMessageDeletionHandlerTests
{
    [Fact]
    public async Task TransientFailure_IsRetriedOnNextAttempt()
    {
        var client = new FakeDeletionClient(new HttpRequestException("temporary"), null);
        var handler = Create(client);

        var first = await handler.DeleteAsync(-100, 42, CancellationToken.None);
        var second = await handler.DeleteAsync(-100, 42, CancellationToken.None);

        Assert.Equal(TelegramMessageDeletionOutcome.Retry, first.Outcome);
        Assert.Equal("temporary", first.Error);
        Assert.Equal(TelegramMessageDeletionOutcome.Success, second.Outcome);
        Assert.Equal(2, client.Attempts);
    }

    [Fact]
    public async Task AlreadyDeletedMessage_IsSuccessfulCleanup()
    {
        var client = new FakeDeletionClient(
            new ApiRequestException("Bad Request: message to delete not found", 400));

        var outcome = await Create(client).DeleteAsync(-100, 42, CancellationToken.None);

        Assert.Equal(TelegramMessageDeletionOutcome.Success, outcome.Outcome);
    }

    private static TelegramMessageDeletionHandler Create(ITelegramMessageDeletionClient client) =>
        new(client, NullLogger<TelegramMessageDeletionHandler>.Instance);

    private sealed class FakeDeletionClient(params Exception?[] results) : ITelegramMessageDeletionClient
    {
        public int Attempts { get; private set; }

        public Task DeleteAsync(long chatId, int messageId, CancellationToken cancellationToken)
        {
            var result = results[Math.Min(Attempts, results.Length - 1)];
            Attempts++;
            return result is null ? Task.CompletedTask : Task.FromException(result);
        }
    }
}
