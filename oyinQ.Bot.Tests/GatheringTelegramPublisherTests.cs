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
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OldLargeGatheringWithGuestsEditsSameMessageWithinCaptionLimit(bool textMessage)
    {
        var handler = new RecordingHandler((method, _) => textMessage && method == "editMessageCaption"
            ? Error("Bad Request: there is no caption in the message to edit") : null);
        var gathering = Gathering();
        gathering.CreatedAt = DateTimeOffset.UtcNow.AddDays(-14);
        gathering.Description = "Новое описание " + new string('я', 200);
        gathering.MaximumPlayers = 12;
        gathering.DesiredPlayers = 10;
        gathering.Status = GatheringStatus.Ready;
        gathering.OrganizerParticipant.DisplayName = new string('О', 160);
        var snapshot = GatheringGameSnapshotSerializer.Deserialize(gathering.GameSnapshotJson);
        gathering.GameSnapshotJson = GatheringGameSnapshotSerializer.Serialize(snapshot with
        {
            Name = new string('И', 300), ImageUrl = "https://example.com/game.jpg"
        });
        for (var i = 0; i < 8; i++)
            gathering.Participants.Add(new GameGatheringParticipant
            {
                Id = i + 1, Status = GatheringParticipationStatus.Confirmed,
                Participant = new Participant { TelegramUserId = i + 1, DisplayName = new string('Я', 100) + "<&> 🎲" }
            });
        gathering.Guests.Add(new GameGatheringGuest { DisplayName = new string('Г', 80) });
        for (var i = 0; i < 10; i++)
            gathering.Expansions.Add(new GameGatheringExpansion { Name = new string('Д', 100), BggId = i + 2 });

        await Publisher(handler).UpdateAsync(gathering,
            new BotCommunity("club", "Клуб", -1001, BotMode.Club, "UTC"), default);

        using var body = JsonDocument.Parse(handler.Bodies.Last());
        var html = body.RootElement.GetProperty(textMessage ? "text" : "caption").GetString()!;
        var visible = System.Xml.Linq.XElement.Parse("<root>" + html + "</root>").Value;
        Assert.InRange(visible.Length, 1, 1024);
        Assert.Contains("Новое описание", visible);
        Assert.Contains("10 / 10–12", visible);
        Assert.Contains("гостей: 1", visible);
        Assert.Contains("Выбрано: 10", visible);
        Assert.Contains("Есть места", visible);
        Assert.Contains("Полный состав", visible);
        Assert.Equal(777, body.RootElement.GetProperty("message_id").GetInt32());
        Assert.DoesNotContain(handler.Methods, x => x.StartsWith("send", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void HtmlMarkupLengthDoesNotPrematurelyHideRoster()
    {
        var gathering = Gathering();
        for (var i = 0; i < 10; i++)
            gathering.Participants.Add(new GameGatheringParticipant
            {
                Status = GatheringParticipationStatus.Confirmed,
                Participant = new Participant { TelegramUserId = 1234567890 + i, DisplayName = "Игрок <&> " + new string('&', 10) }
            });
        var html = new GatheringPresentationService().BuildTelegramAnnouncement(gathering,
            new BotCommunity("club", "Клуб", -1001, BotMode.Club, "UTC")).HtmlText;
        Assert.True(html.Length > 1024);
        Assert.True(System.Xml.Linq.XElement.Parse("<root>" + html + "</root>").Value.Length <= 1024);
        Assert.DoesNotContain("Полный состав", html);
        Assert.Contains("tg://user?id=1234567899", html);
    }

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
        var button = Assert.Single(KeyboardButtons(handler.Bodies.Last()));
        Assert.Equal("Открыть сбор", button.Text);
        Assert.DoesNotContain("boardgamegeek.com", handler.Bodies.Last());
        Assert.DoesNotContain("c-1-club", handler.Bodies.Last());
    }

    [Fact]
    public async Task MissingBggId_StillRendersOnlyGatheringButton()
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

        var button = Assert.Single(KeyboardButtons(handler.Bodies.Last()));
        Assert.Equal("Открыть сбор", button.Text);
        Assert.DoesNotContain("boardgamegeek.com", handler.Bodies.Last());
    }

    [Fact]
    public async Task UnchangedPhotoCaption_IsSuccessfulWithoutTextFallback()
    {
        var handler = new RecordingHandler((method, _) => method == "editMessageCaption"
            ? Error("Bad Request: message is not modified")
            : null);
        var publisher = Publisher(handler);

        await publisher.UpdateAsync(Gathering(),
            new BotCommunity("club", "Клуб", -1001, BotMode.Club, "UTC"), default);

        Assert.Equal(1, handler.Methods.Count(method => method == "editMessageCaption"));
        Assert.DoesNotContain("editMessageText", handler.Methods);
    }

    [Fact]
    public async Task TextAnnouncement_FallsBackOnlyWhenCaptionIsUnavailable()
    {
        var handler = new RecordingHandler((method, _) => method == "editMessageCaption"
            ? Error("Bad Request: there is no caption in the message to edit")
            : null);
        var publisher = Publisher(handler);

        await publisher.UpdateAsync(Gathering(),
            new BotCommunity("club", "Клуб", -1001, BotMode.Club, "UTC"), default);

        Assert.Contains("editMessageCaption", handler.Methods);
        Assert.Contains("editMessageText", handler.Methods);
    }

    private static GatheringTelegramPublisher Publisher(HttpMessageHandler handler)
    {
        var bot = new TelegramBotClient("123456:abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNO",
            new HttpClient(handler));
        return new GatheringTelegramPublisher(new RejectingGroupSender(), bot,
            new GatheringPresentationService(), NullLogger<GatheringTelegramPublisher>.Instance);
    }

    private static HttpResponseMessage Error(string description) => new(HttpStatusCode.BadRequest)
    {
        Content = new StringContent(
            JsonSerializer.Serialize(new { ok = false, error_code = 400, description }),
            Encoding.UTF8, "application/json")
    };

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

    private sealed class RecordingHandler(
        Func<string, int, HttpResponseMessage?>? response = null) : HttpMessageHandler
    {
        public List<string> Methods { get; } = [];
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var method = request.RequestUri!.Segments.Last();
            Methods.Add(method);
            Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
            if (response?.Invoke(method, Methods.Count) is { } configured) return configured;
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
