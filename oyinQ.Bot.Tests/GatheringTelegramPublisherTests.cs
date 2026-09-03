using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Gatherings;
using oyinQ.Bot.Integrations.Telegram;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace oyinQ.Bot.Tests;

public sealed class GatheringTelegramPublisherTests
{
    [Fact]
    public async Task UpdateEditsOriginalMessageWithoutResolvingOrRepostingDestination()
    {
        var handler = new RecordingHandler();
        var bot = new TelegramBotClient("123456:abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNO",
            new HttpClient(handler));
        var newMessages = new RejectingGroupSender();
        var publisher = new GatheringTelegramPublisher(newMessages, bot,
            new GatheringPresentationService(), NullLogger<GatheringTelegramPublisher>.Instance);
        var gathering = Gathering();

        await publisher.UpdateAsync(gathering,
            new BotCommunity("club", "Клуб", -1001, BotMode.Club, "UTC"), default);

        Assert.Equal(0, newMessages.Attempts);
        Assert.Contains(handler.Methods, method => method == "getMe");
        Assert.Contains(handler.Methods, method => method == "editMessageCaption");
        Assert.DoesNotContain(handler.Methods, method => method.StartsWith("send", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("\"chat_id\":-1001", handler.Bodies.Last());
        Assert.Contains("\"message_id\":777", handler.Bodies.Last());
        Assert.Contains("https://boardgamegeek.com/boardgame/1", handler.Bodies.Last());
        Assert.Contains("BGG", handler.Bodies.Last());
        Assert.Contains("c-1-club", handler.Bodies.Last());
        Assert.Contains(KeyboardButtons(handler.Bodies.Last()), button => button.Text == "В коллекции");
    }

    [Fact]
    public async Task MissingBggId_DoesNotRenderBrokenButton()
    {
        var handler = new RecordingHandler();
        var bot = new TelegramBotClient("123456:abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNO",
            new HttpClient(handler));
        var publisher = new GatheringTelegramPublisher(new RejectingGroupSender(), bot,
            new GatheringPresentationService(), NullLogger<GatheringTelegramPublisher>.Instance);
        var gathering = Gathering();
        var snapshot = GatheringGameSnapshotSerializer.Deserialize(gathering.GameSnapshotJson);
        gathering.GameSnapshotJson = GatheringGameSnapshotSerializer.Serialize(snapshot with { BggId = null });

        await publisher.UpdateAsync(gathering,
            new BotCommunity("club", "Клуб", -1001, BotMode.Club, "UTC"), default);

        Assert.DoesNotContain("boardgamegeek.com", handler.Bodies.Last());
        Assert.DoesNotContain("\"text\":\"BGG\"", handler.Bodies.Last());
        Assert.DoesNotContain(KeyboardButtons(handler.Bodies.Last()), button => button.Text == "В коллекции");
    }

    private static GameGathering Gathering() => new()
    {
        PublicId = Guid.NewGuid(), CommunityKey = "club", TelegramChatId = -1001, TelegramMessageId = 777,
        StartsAtUtc = DateTimeOffset.UtcNow.AddDays(1), MinimumPlayers = 2, DesiredPlayers = 3,
        MaximumPlayers = 4, Status = GatheringStatus.Recruiting,
        GameSnapshotJson = GatheringGameSnapshotSerializer.Serialize(new GatheringGameSnapshot(
            GatheringGameSnapshot.CurrentVersion, 1, "Игра", null, null, 2, 4, null, [], "catalog", [])),
        OrganizerParticipant = new Participant { DisplayName = "Организатор" }
    };

    private static IReadOnlyList<(string Text, string? Url)> KeyboardButtons(string body)
    {
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("reply_markup").GetProperty("inline_keyboard")
            .EnumerateArray().SelectMany(row => row.EnumerateArray())
            .Select(button => (button.GetProperty("text").GetString()!,
                button.TryGetProperty("url", out var url) ? url.GetString() : null)).ToArray();
    }

    private sealed class RejectingGroupSender : ITelegramGroupMessageSender
    {
        public int Attempts { get; private set; }
        public Task<Message> SendMessageAsync(string key, string text, ParseMode mode, ReplyMarkup? markup,
            CancellationToken token) { Attempts++; throw new InvalidOperationException(); }
        public Task<Message> SendPhotoAsync(string key, InputFile photo, string caption, ParseMode mode,
            ReplyMarkup? markup, CancellationToken token) { Attempts++; throw new InvalidOperationException(); }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<string> Methods { get; } = [];
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var method = request.RequestUri!.Segments.Last();
            Methods.Add(method);
            Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
            var body = method == "getMe"
                ? "{\"ok\":true,\"result\":{\"id\":123456,\"is_bot\":true,\"first_name\":\"OyinQ\",\"username\":\"oyinq_test_bot\"}}"
                : "{\"ok\":true,\"result\":{\"message_id\":777,\"date\":0,\"chat\":{\"id\":-1001,\"type\":\"supergroup\"}}}";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        }
    }
}
