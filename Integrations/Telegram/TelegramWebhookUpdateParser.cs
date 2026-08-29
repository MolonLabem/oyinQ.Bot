using System.Text.Json;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace oyinQ.Bot.Integrations.Telegram;

public sealed class TelegramWebhookUpdateParser(ILogger<TelegramWebhookUpdateParser> logger)
{
    public async Task<Update?> ParseAsync(Stream body, CancellationToken cancellationToken = default)
    {
        try
        {
            return await JsonSerializer.DeserializeAsync<Update>(
                body,
                JsonBotAPI.Options,
                cancellationToken);
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "Telegram webhook payload could not be deserialized.");
            return null;
        }
    }
}
