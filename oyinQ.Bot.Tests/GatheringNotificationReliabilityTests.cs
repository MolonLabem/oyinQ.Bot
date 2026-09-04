using oyinQ.Bot.Features.Notifications;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Communities;
using oyinQ.Bot.Features.Gatherings;
using oyinQ.Bot.Integrations.Telegram;
using Telegram.Bot;

namespace oyinQ.Bot.Tests;

public sealed class GatheringNotificationReliabilityTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ConfirmedLeave_PromotesFifoAndNotifiesPromotedParticipantAndOrganizerOnce()
    {
        await using var fixture = await ClubFixture.CreateAsync(2, 2, confirmed: 1, waitlisted: 2);

        var result = await fixture.Service.LeaveAsync(fixture.Gathering.PublicId, "club",
            fixture.Confirmed[0].TelegramUserId, Now, default);
        await NotifyAsync(fixture.Notifications, result);

        await DeliverAsync(fixture.Db, fixture.Telegram);

        Assert.Equal(GatheringParticipationStatus.Withdrawn, Membership(fixture, fixture.Confirmed[0]).Status);
        Assert.Equal(GatheringParticipationStatus.Confirmed, Membership(fixture, fixture.Waitlisted[0]).Status);
        Assert.Equal(GatheringParticipationStatus.Waitlisted, Membership(fixture, fixture.Waitlisted[1]).Status);
        Assert.Equal(2, fixture.Telegram.Requests.Count);
        var promoted = Assert.Single(fixture.Telegram.Requests, x =>
            x.ChatId == fixture.Waitlisted[0].TelegramUserId);
        Assert.Contains("для вас освободилось место", promoted.Text);
        AssertGatheringButton(promoted, fixture.Gathering.PublicId, "club");
        var organizer = Assert.Single(fixture.Telegram.Requests, x =>
            x.ChatId == fixture.Organizer.TelegramUserId);
        Assert.Contains("Его место занял Участник 2 из листа ожидания", organizer.Text);
        Assert.DoesNotContain("Сейчас участников", organizer.Text);
    }

    [Fact]
    public async Task ConfirmedLeave_WithoutWaitlistAndBelowMinimum_WarnsOrganizerWithCanonicalCounts()
    {
        await using var fixture = await ClubFixture.CreateAsync(3, 4, confirmed: 2);

        var result = await fixture.Service.LeaveAsync(fixture.Gathering.PublicId, "club",
            fixture.Confirmed[0].TelegramUserId, Now, default);
        await NotifyAsync(fixture.Notifications, result);

        await DeliverAsync(fixture.Db, fixture.Telegram);
        var warning = Assert.Single(fixture.Telegram.Requests);

        Assert.Equal(fixture.Organizer.TelegramUserId, warning.ChatId);
        Assert.Contains("Сейчас участников: 2", warning.Text);
        Assert.Contains("Минимум для игры: 3", warning.Text);
        Assert.Contains("Нужно найти ещё 1", warning.Text);
        AssertGatheringButton(warning, fixture.Gathering.PublicId, "club");
    }

    [Fact]
    public async Task ConfirmedLeave_WithoutWaitlistButStillMeetingMinimum_RespectsDisabledOptionalLeaveNotice()
    {
        await using var fixture = await ClubFixture.CreateAsync(2, 4, confirmed: 1, guests: 1);

        var result = await fixture.Service.LeaveAsync(fixture.Gathering.PublicId, "club",
            fixture.Confirmed[0].TelegramUserId, Now, default);
        await NotifyAsync(fixture.Notifications, result);

        await DeliverAsync(fixture.Db, fixture.Telegram);

        Assert.Empty(fixture.Telegram.Requests);
        Assert.Equal(2, GatheringCapacity.OccupiedSeats(fixture.Gathering));
    }

    [Fact]
    public async Task WaitlistedParticipantLeaves_WithoutPromotionOrOrganizerNotification()
    {
        await using var fixture = await ClubFixture.CreateAsync(2, 2, confirmed: 1, waitlisted: 2);

        var result = await fixture.Service.LeaveAsync(fixture.Gathering.PublicId, "club",
            fixture.Waitlisted[0].TelegramUserId, Now, default);
        await NotifyAsync(fixture.Notifications, result);

        await DeliverAsync(fixture.Db, fixture.Telegram);

        Assert.Equal(GatheringParticipationStatus.Withdrawn, Membership(fixture, fixture.Waitlisted[0]).Status);
        Assert.Equal(GatheringParticipationStatus.Waitlisted, Membership(fixture, fixture.Waitlisted[1]).Status);
        Assert.Empty(fixture.Telegram.Requests);
    }

    [Fact]
    public async Task RepeatedLeave_IsNoOpAndDoesNotDuplicateNotifications()
    {
        await using var fixture = await ClubFixture.CreateAsync(2, 2, confirmed: 1, waitlisted: 1);
        var telegramUserId = fixture.Confirmed[0].TelegramUserId;

        var first = await fixture.Service.LeaveAsync(fixture.Gathering.PublicId, "club", telegramUserId, Now, default);
        await NotifyAsync(fixture.Notifications, first);
        var second = await fixture.Service.LeaveAsync(fixture.Gathering.PublicId, "club", telegramUserId,
            Now.AddSeconds(1), default);
        await NotifyAsync(fixture.Notifications, second);

        await DeliverAsync(fixture.Db, fixture.Telegram);

        Assert.NotNull(first.Withdrawal);
        Assert.Null(second.Withdrawal);
        Assert.Equal(2, fixture.Telegram.Requests.Count);
    }

    [Fact]
    public async Task GuestRemoval_PromotesFifoAndOnlyNotifiesPromotedParticipant()
    {
        await using var fixture = await ClubFixture.CreateAsync(2, 2, waitlisted: 2, guests: 1);
        var guestId = fixture.Gathering.Guests.Single().Id;

        var result = await fixture.Service.RemoveGuestAsync(fixture.Gathering.PublicId, guestId, "club",
            fixture.Organizer.TelegramUserId, Now, default);
        await fixture.Notifications.NotifyPromotionsAsync("club", fixture.Gathering.PublicId,
            result.Promotion is null ? [] : [result.Promotion], default);

        await DeliverAsync(fixture.Db, fixture.Telegram);

        Assert.Equal(GatheringParticipationStatus.Confirmed, Membership(fixture, fixture.Waitlisted[0]).Status);
        Assert.Equal(GatheringParticipationStatus.Waitlisted, Membership(fixture, fixture.Waitlisted[1]).Status);
        var notification = Assert.Single(fixture.Telegram.Requests);
        Assert.Equal(fixture.Waitlisted[0].TelegramUserId, notification.ChatId);
        Assert.DoesNotContain(fixture.Telegram.Requests, x => x.ChatId == fixture.Organizer.TelegramUserId);
    }

    [Fact]
    public async Task CapacityIncrease_NotifiesEveryActuallyPromotedParticipantExactlyOnce()
    {
        await using var fixture = await ClubFixture.CreateAsync(1, 2, confirmed: 1, waitlisted: 3,
            snapshotMaximum: 6);
        var management = new GatheringManagementService(fixture.Db, null!,
            new CampParticipationPolicy(fixture.Db, fixture.TimeProvider), fixture.Notifications,
            fixture.TimeProvider);

        var result = await management.UpdateAsync(fixture.Gathering.PublicId, "club",
            fixture.Organizer.TelegramUserId,
            new(fixture.Gathering.StartsAtUtc, 1, 4, 4, null, true, []), default);
        await fixture.Notifications.NotifyPromotionsAsync("club", fixture.Gathering.PublicId,
            result.Promotions, default);

        await DeliverAsync(fixture.Db, fixture.Telegram);

        Assert.Equal(2, result.Promotions.Count);
        Assert.Equal([fixture.Waitlisted[0].TelegramUserId, fixture.Waitlisted[1].TelegramUserId],
            fixture.Telegram.Requests.Select(x => x.ChatId));
        Assert.Equal(GatheringParticipationStatus.Waitlisted, Membership(fixture, fixture.Waitlisted[2]).Status);
    }

    [Fact]
    public async Task CampDateRemoval_WithdrawsConfirmedParticipantAndSendsReplacementNotifications()
    {
        await using var fixture = await CampFixture.CreateAsync(twoGatherings: false);

        var result = await fixture.Registrations.SaveAsync(fixture.Camp.Id, fixture.Departing.Id,
            [fixture.SecondDate], false, "Уходящий", "Город", true, default);
        foreach (var withdrawal in result.Withdrawals)
            await fixture.Notifications.NotifyWithdrawalAsync(withdrawal, default);

        var gathering = fixture.Gatherings.Single();
        await DeliverAsync(fixture.Db, fixture.Telegram);

        Assert.Equal(GatheringParticipationStatus.Withdrawn,
            gathering.Participants.Single(x => x.ParticipantId == fixture.Departing.Id).Status);
        Assert.Equal(GatheringParticipationStatus.Confirmed,
            gathering.Participants.Single(x => x.ParticipantId == fixture.Waitlisted.Id).Status);
        Assert.Equal(2, fixture.Telegram.Requests.Count);
        Assert.Single(fixture.Telegram.Requests, x => x.ChatId == fixture.Waitlisted.TelegramUserId);
        Assert.Single(fixture.Telegram.Requests, x => x.ChatId == fixture.Organizer.TelegramUserId);
    }

    [Fact]
    public async Task CampUnregister_AppliesReplacementAndUnderfilledSemanticsToEveryImpactedGathering()
    {
        await using var fixture = await CampFixture.CreateAsync(twoGatherings: true);

        var result = await fixture.Registrations.UnregisterAsync(fixture.Camp.Id, fixture.Departing.Id, default);
        foreach (var withdrawal in result.Withdrawals)
            await fixture.Notifications.NotifyWithdrawalAsync(withdrawal, default);

        await DeliverAsync(fixture.Db, fixture.Telegram);

        Assert.Equal(2, result.Withdrawals.Count);
        Assert.All(fixture.Gatherings, gathering => Assert.Equal(GatheringParticipationStatus.Withdrawn,
            gathering.Participants.Single(x => x.ParticipantId == fixture.Departing.Id).Status));
        Assert.Equal(3, fixture.Telegram.Requests.Count);
        Assert.Single(fixture.Telegram.Requests, x => x.ChatId == fixture.Waitlisted.TelegramUserId);
        Assert.Equal(2, fixture.Telegram.Requests.Count(x => x.ChatId == fixture.Organizer.TelegramUserId));
        Assert.Single(fixture.Telegram.Requests, x => x.Text.Contains("Нужно найти ещё 1"));
    }

    [Fact]
    public async Task TelegramFailure_IsLoggedAndDoesNotUndoCommittedMutation()
    {
        await using var fixture = await ClubFixture.CreateAsync(2, 2, confirmed: 1, waitlisted: 1,
            rejectTelegram: true);

        var result = await fixture.Service.LeaveAsync(fixture.Gathering.PublicId, "club",
            fixture.Confirmed[0].TelegramUserId, Now, default);
        await fixture.Notifications.NotifyWithdrawalAsync(result.Withdrawal!, default);

        fixture.Db.ChangeTracker.Clear();
        var rows = await fixture.Db.GameGatheringParticipants.AsNoTracking().ToArrayAsync();
        await DeliverAsync(fixture.Db, fixture.Telegram);

        Assert.Equal(GatheringParticipationStatus.Withdrawn,
            rows.Single(x => x.ParticipantId == fixture.Confirmed[0].Id).Status);
        Assert.Equal(GatheringParticipationStatus.Confirmed,
            rows.Single(x => x.ParticipantId == fixture.Waitlisted[0].Id).Status);
        Assert.Equal(2, fixture.Telegram.Requests.Count);
        Assert.Equal(2, await fixture.Db.Notifications.CountAsync(x => x.State == NotificationState.CannotMessageUser));
    }

    private static async Task NotifyAsync(GatheringNotificationService notifications,
        GatheringMutationResult result)
    {
        if (result.Withdrawal is not null)
            await notifications.NotifyWithdrawalAsync(result.Withdrawal, default);
    }

    private static GameGatheringParticipant Membership(ClubFixture fixture, Participant participant) =>
        fixture.Gathering.Participants.Single(x => x.ParticipantId == participant.Id);

    private static void AssertGatheringButton(TelegramRequest request, Guid publicId, string communityKey)
    {
        Assert.Equal("Открыть сбор", request.ButtonText);
        Assert.Equal($"https://example.test/app/?community={communityKey}&gathering={publicId}", request.ButtonUrl);
    }

    private sealed class ClubFixture : IAsyncDisposable
    {
        private ClubFixture(AppDbContext db, Participant organizer, Participant[] confirmed,
            Participant[] waitlisted, GameGathering gathering, RecordingTelegramHandler telegram,
            ListLogger<GatheringNotificationService> logger, FixedTimeProvider timeProvider)
        {
            Db = db;
            Organizer = organizer;
            Confirmed = confirmed;
            Waitlisted = waitlisted;
            Gathering = gathering;
            Telegram = telegram;
            Logger = logger;
            TimeProvider = timeProvider;
            Service = new GatheringService(db, new CampParticipationPolicy(db, timeProvider));
            Notifications = CreateNotifications(db, telegram, logger);
        }

        public AppDbContext Db { get; }
        public Participant Organizer { get; }
        public Participant[] Confirmed { get; }
        public Participant[] Waitlisted { get; }
        public GameGathering Gathering { get; }
        public RecordingTelegramHandler Telegram { get; }
        public ListLogger<GatheringNotificationService> Logger { get; }
        public FixedTimeProvider TimeProvider { get; }
        public GatheringService Service { get; }
        public GatheringNotificationService Notifications { get; }

        public static async Task<ClubFixture> CreateAsync(int minimum, int maximum, int confirmed = 0,
            int waitlisted = 0, int guests = 0, int? snapshotMaximum = null, bool rejectTelegram = false)
        {
            var db = CreateDb();
            var community = new OyinQCommunity
            {
                Key = "club", Name = "Клуб", Mode = BotMode.Club, TimeZoneId = "UTC", IsActive = true
            };
            var organizer = Participant(100, "Организатор");
            var confirmedParticipants = Enumerable.Range(0, confirmed)
                .Select(i => Participant(200 + i, $"Участник {i + 1}")).ToArray();
            var waitlistedParticipants = Enumerable.Range(0, waitlisted)
                .Select(i => Participant(300 + i, $"Участник {i + 2}")).ToArray();
            db.Add(community);
            db.Add(organizer);
            db.AddRange(confirmedParticipants);
            db.AddRange(waitlistedParticipants);
            await db.SaveChangesAsync();
            var gathering = Gathering("club", organizer, minimum, maximum, snapshotMaximum ?? maximum, Now.AddDays(2));
            foreach (var (participant, index) in confirmedParticipants.Select((value, index) => (value, index)))
                gathering.Participants.Add(Membership(participant, GatheringParticipationStatus.Confirmed,
                    Now.AddMinutes(index)));
            foreach (var (participant, index) in waitlistedParticipants.Select((value, index) => (value, index)))
                gathering.Participants.Add(Membership(participant, GatheringParticipationStatus.Waitlisted,
                    Now.AddHours(1).AddMinutes(index)));
            for (var i = 0; i < guests; i++)
                gathering.Guests.Add(new GameGatheringGuest
                {
                    DisplayName = $"Гость {i + 1}", CreatedByParticipantId = organizer.Id,
                    CreatedAt = Now, UpdatedAt = Now
                });
            GatheringCapacity.SynchronizeScheduledStatus(gathering);
            db.GameGatherings.Add(gathering);
            await db.SaveChangesAsync();
            var telegram = new RecordingTelegramHandler(rejectTelegram);
            return new(db, organizer, confirmedParticipants, waitlistedParticipants, gathering,
                telegram, new(), new(Now));
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class CampFixture : IAsyncDisposable
    {
        private CampFixture(AppDbContext db, Camp camp, Participant organizer, Participant departing,
            Participant waitlisted, GameGathering[] gatherings, RecordingTelegramHandler telegram,
            FixedTimeProvider timeProvider)
        {
            Db = db;
            Camp = camp;
            Organizer = organizer;
            Departing = departing;
            Waitlisted = waitlisted;
            Gatherings = gatherings;
            Telegram = telegram;
            Registrations = new(db, timeProvider);
            Notifications = CreateNotifications(db, telegram, new ListLogger<GatheringNotificationService>());
        }

        public DateOnly FirstDate { get; } = new(2026, 9, 10);
        public DateOnly SecondDate { get; } = new(2026, 9, 11);
        public AppDbContext Db { get; }
        public Camp Camp { get; }
        public Participant Organizer { get; }
        public Participant Departing { get; }
        public Participant Waitlisted { get; }
        public GameGathering[] Gatherings { get; }
        public RecordingTelegramHandler Telegram { get; }
        public CampRegistrationService Registrations { get; }
        public GatheringNotificationService Notifications { get; }

        public static async Task<CampFixture> CreateAsync(bool twoGatherings)
        {
            var db = CreateDb();
            var community = new OyinQCommunity
            {
                Key = "camp", Name = "Кэмп", Mode = BotMode.Camp, TimeZoneId = "UTC", IsActive = true
            };
            var camp = new Camp
            {
                BotChat = community, BotChatKey = "camp", Name = "Кэмп", Status = CampStatus.Active,
                StartsAtUtc = new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero), EndsAtUtc = new DateTimeOffset(2026, 9, 11, 0, 0, 0, TimeSpan.Zero).AddDays(1)
            };
            var organizer = Participant(100, "Организатор");
            var departing = Participant(200, "Уходящий");
            var waitlisted = Participant(300, "Ожидающий");
            db.AddRange(community, camp, organizer, departing, waitlisted);
            await db.SaveChangesAsync();
            var registration = new CampRegistration
            {
                CampId = camp.Id, ParticipantId = departing.Id, City = "Город", NeedsAccommodation = false,
                DaysStaying = 2, DisplayName = "Уходящий", CreatedAt = Now, UpdatedAt = Now,
                SelectedDays =
                [
                    new CampRegistrationDay { Date = new(2026, 9, 10) },
                    new CampRegistrationDay { Date = new(2026, 9, 11) }
                ]
            };
            db.CampRegistrations.Add(registration);
            var first = Gathering("camp", organizer, 2, 2, 2,
                new DateTimeOffset(2026, 9, 10, 18, 0, 0, TimeSpan.Zero));
            first.Participants.Add(Membership(departing, GatheringParticipationStatus.Confirmed, Now));
            first.Participants.Add(Membership(waitlisted, GatheringParticipationStatus.Waitlisted, Now.AddMinutes(1)));
            var gatherings = new List<GameGathering> { first };
            if (twoGatherings)
            {
                var second = Gathering("camp", organizer, 2, 3, 3,
                    new DateTimeOffset(2026, 9, 11, 18, 0, 0, TimeSpan.Zero));
                second.Participants.Add(Membership(departing, GatheringParticipationStatus.Confirmed, Now));
                gatherings.Add(second);
            }
            db.GameGatherings.AddRange(gatherings);
            await db.SaveChangesAsync();
            return new(db, camp, organizer, departing, waitlisted, gatherings.ToArray(),
                new(false), new(Now));
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private static AppDbContext CreateDb() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);

    private static Participant Participant(long telegramUserId, string displayName) => new()
    {
        TelegramUserId = telegramUserId, DisplayName = displayName
    };

    private static GameGatheringParticipant Membership(Participant participant,
        GatheringParticipationStatus status, DateTimeOffset joinedAt) => new()
    {
        ParticipantId = participant.Id, Participant = participant, Status = status, JoinedAt = joinedAt
    };

    private static GameGathering Gathering(string communityKey, Participant organizer, int minimum,
        int maximum, int snapshotMaximum, DateTimeOffset startsAt) => new()
    {
        PublicId = Guid.NewGuid(), CommunityKey = communityKey,
        OrganizerParticipantId = organizer.Id, OrganizerParticipant = organizer,
        StartsAtUtc = startsAt, MinimumPlayers = minimum, DesiredPlayers = maximum,
        MaximumPlayers = maximum, Status = GatheringStatus.Recruiting,
        GameSnapshotJson = GatheringGameSnapshotSerializer.Serialize(new GatheringGameSnapshot(
            GatheringGameSnapshot.CurrentVersion, 42, "Игра", null, null, 1, snapshotMaximum,
            null, [], "catalog", [])),
        Participants = [], Guests = []
    };

    private static GatheringNotificationService CreateNotifications(AppDbContext db,
        RecordingTelegramHandler telegram, ILogger<GatheringNotificationService> logger)
    {
        var bot = new TelegramBotClient("123456:abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNO",
            new HttpClient(telegram));
        var links = new MiniAppLinkBuilder(Options.Create(new BotOptions
        {
            PublicBaseUrl = "https://example.test"
        }));
        return new(db, new NotificationService(db, new FixedTimeProvider(Now)));
    }

    private static async Task DeliverAsync(AppDbContext db, RecordingTelegramHandler telegram)
    {
        foreach (var p in await db.Participants.ToArrayAsync())
        {
            p.PrivateChatStartedAt = Now;
            if (!await db.NotificationPreferences.AnyAsync(x => x.ParticipantId == p.Id))
                db.NotificationPreferences.Add(new() { ParticipantId = p.Id, GatheringDetailsChanged = false, OrganizerParticipantLeft = false });
        }
        await db.SaveChangesAsync();
        var bot = new TelegramBotClient("123456:abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNO", new HttpClient(telegram));
        var links = new MiniAppLinkBuilder(Options.Create(new BotOptions { PublicBaseUrl = "https://example.test" }));
        var dispatcher = new NotificationDispatcher(db, new FixedTimeProvider(DateTimeOffset.UtcNow > Now ? DateTimeOffset.UtcNow : Now), new TelegramNotificationTransport(bot, links));
        for (var i = 0; i < 20 && await dispatcher.ProcessOneAsync(default); i++) { }
    }

    private sealed record TelegramRequest(long ChatId, string Text, string? ButtonText, string? ButtonUrl);

    private sealed class RecordingTelegramHandler(bool reject) : HttpMessageHandler
    {
        public List<TelegramRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var chatId = root.GetProperty("chat_id").GetInt64();
            var text = root.GetProperty("text").GetString()!;
            string? buttonText = null;
            string? buttonUrl = null;
            if (root.TryGetProperty("reply_markup", out var markup))
            {
                var button = markup.GetProperty("inline_keyboard")[0][0];
                buttonText = button.GetProperty("text").GetString();
                buttonUrl = button.GetProperty("web_app").GetProperty("url").GetString();
            }
            Requests.Add(new(chatId, text, buttonText, buttonUrl));
            return reject
                ? Json(HttpStatusCode.Forbidden,
                    "{\"ok\":false,\"error_code\":403,\"description\":\"Forbidden: bot was blocked by the user\"}")
                : Json(HttpStatusCode.OK,
                    $"{{\"ok\":true,\"result\":{{\"message_id\":1,\"date\":0,\"chat\":{{\"id\":{chatId},\"type\":\"private\",\"first_name\":\"User\"}}}}}}");
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<string> Warnings { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Warning) Warnings.Add(formatter(state, exception));
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
