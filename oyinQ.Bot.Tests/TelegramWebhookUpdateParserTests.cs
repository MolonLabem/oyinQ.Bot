using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using oyinQ.Bot.Integrations.Telegram;
using Telegram.Bot.Types.Enums;

namespace oyinQ.Bot.Tests;

public sealed class TelegramWebhookUpdateParserTests
{
    private readonly TelegramWebhookUpdateParser parser =
        new(NullLogger<TelegramWebhookUpdateParser>.Instance);


    [Fact]
    public async Task ParseAsync_ParsesPrivateStartMessage()
    {
        const string json = """
            {
              "update_id": 10000,
              "message": {
                "message_id": 1365,
                "date": 1441645532,
                "chat": {
                  "id": 1111111,
                  "type": "private",
                  "first_name": "Test"
                },
                "from": {
                  "id": 1111111,
                  "is_bot": false,
                  "first_name": "Test"
                },
                "text": "/start",
                "entities": [
                  {
                    "offset": 0,
                    "length": 6,
                    "type": "bot_command"
                  }
                ]
              }
            }
            """;

        await using var body = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var update = await parser.ParseAsync(body);

        Assert.NotNull(update);
        Assert.Equal(10000, update.Id);
        Assert.Equal("/start", update.Message?.Text);
        Assert.Equal(ChatType.Private, update.Message?.Chat.Type);
        Assert.Equal(MessageEntityType.BotCommand, Assert.Single(update.Message!.Entities!).Type);
    }

    [Fact]
    public async Task ParseAsync_ReturnsNullForMalformedPayload()
    {
        await using var body = new MemoryStream("{"u8.ToArray());

        var update = await parser.ParseAsync(body);

        Assert.Null(update);
    }
}
