using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Admin;
using Telegram.Bot;

namespace oyinQ.Bot.Tests;

public sealed class CampParticipantAdminServiceTests
{
    [Fact]
    public async Task AuthorizedAdminSeesExactRegistrationAndTelegramContact()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var community = new OyinQCommunity
        {
            Key = "camp", Name = "Кэмп", TelegramChatId = -1001, Mode = BotMode.Camp,
            TimeZoneId = "UTC", IsActive = true, CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var camp = new Camp
        {
            BotChat = community, BotChatKey = community.Key, Name = community.Name,
            Status = CampStatus.Active, StartsAtUtc = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), EndsAtUtc = new DateTimeOffset(2026, 9, 3, 0, 0, 0, TimeSpan.Zero).AddDays(1),
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };
        var participant = new Participant
        {
            TelegramUserId = 700, TelegramUsername = "player", DisplayName = "Telegram Name",
            PreferredDisplayName = "Игрок", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };
        var registration = new CampRegistration
        {
            Camp = camp, Participant = participant, DisplayName = "Алексей", City = "Алматы",
            NeedsAccommodation = true, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
            SelectedDays =
            [
                new CampRegistrationDay { Date = new(2026, 9, 3) },
                new CampRegistrationDay { Date = new(2026, 9, 1) }
            ]
        };
        db.CampRegistrations.Add(registration);
        await db.SaveChangesAsync();
        var authorization = new AdminAuthorizationService(db, new NoTelegramAdmins(),
            Options.Create(new AdministrationOptions
            {
                SuperAdminTelegramUserIds = new HashSet<long> { 42 }
            }), TimeProvider.System);
        var bot = new TelegramBotClient("123456:abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNO",
            new HttpClient(new NeverCalledHandler()));
        var service = new CampParticipantAdminService(db, authorization, bot,
            NullLogger<CampParticipantAdminService>.Instance);

        var result = await service.GetAsync(42, camp.Id, default);

        var item = Assert.Single(result.Participants);
        Assert.Equal("Алексей", item.DisplayName);
        Assert.Equal("Алматы", item.City);
        Assert.Equal([new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 3)], item.SelectedDates);
        Assert.True(item.NeedsAccommodation);
        Assert.Equal("player", item.TelegramUsername);
        Assert.Equal("https://t.me/player?profile", item.ContactUrl);
    }

    private sealed class NoTelegramAdmins : ITelegramChatAdministratorVerifier
    {
        public Task<bool> IsAdministratorAsync(long telegramChatId, long telegramUserId,
            CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<IReadOnlyList<EligibleGroupAdministrator>> GetAdministratorsAsync(long telegramChatId,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<EligibleGroupAdministrator>>([]);
    }

    private sealed class NeverCalledHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }
}
