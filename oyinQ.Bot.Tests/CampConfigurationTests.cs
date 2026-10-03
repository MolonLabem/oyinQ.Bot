using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using oyinQ.Bot.Data;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Communities;

namespace oyinQ.Bot.Tests;

public sealed class CampConfigurationTests
{
    internal static CampConfiguration Configuration() => CampConfigurationRules.Normalize(new(
        Description: "🎃 Хэллоуин\nИгры и костюмы", LocationName: "Дом", LocationUrl: "https://example.test/map",
        PaymentInstructions: "Перевод организатору", Pricing: new("KZT", 10000, EarlyAmount: 7500, EarlyUntil: new(2026, 10, 27)),
        RegistrationFields: [new("arrival", "Приезд", "Choice", true, Options: [new("noon", "12:00"), new("evening", "18:00")]),
            new("stay", "Ночёвка", "Checkbox", Amount: 1000, PerDay: true), new("food", "Еда", "Choice", Options: [new("own", "С собой"), new("bbq", "Шашлыки", 1500)]),
            new("comment", "Комментарий", "Multiline"), new("consent", "Согласен с правилами", "Checkbox", true)]));
    internal static Dictionary<string, string> Answers() => new() { ["arrival"] = "evening", ["stay"] = "true", ["food"] = "bbq", ["comment"] = "  🎲 Привезу игру\nещё строка  ", ["consent"] = "true" };

    [Fact]
    public void QuoteUsesLocalInclusiveDeadlineSelectedDaysAndServerOptionPrices()
    {
        var config = Configuration(); var answers = CampConfigurationRules.ValidateAnswers(config, Answers());
        var before = DateTimeOffset.Parse("2026-10-27T18:59:59Z");
        var early = CampConfigurationRules.Quote(config, answers, 2, "Asia/Qyzylorda", before)!;
        Assert.Equal(11000, early.Total); Assert.Equal([7500m, 2000m, 1500m], early.Lines.Select(line => line.Amount));
        Assert.Equal(2, early.Lines[1].Quantity); Assert.Equal(1000, early.Lines[1].UnitAmount);
        Assert.Equal(13500, CampConfigurationRules.Quote(config, answers, 2, "Asia/Qyzylorda", before.AddSeconds(1))!.Total);
        var housing = config with { Pricing = config.Pricing! with { AccommodationAmount = 500, AccommodationPerDay = true } };
        Assert.Equal(12000, CampConfigurationRules.Quote(housing, answers, 2, "Asia/Qyzylorda", before, needsAccommodation: true)!.Total);
        var daily = config with { Pricing = config.Pricing! with { PerDay = true } };
        Assert.Equal(18500, CampConfigurationRules.Quote(daily, answers, 2, "UTC", before)!.Total);
        Assert.Null(CampConfigurationRules.Quote(CampConfigurationRules.Normalize(null), new Dictionary<string, string>(), 2, "UTC", before));
    }

    [Fact]
    public void RequiredAndUnknownAnswersAreRejectedWhileQuoteAcceptsIncompleteForm()
    {
        var config = Configuration();
        Assert.Throws<ArgumentException>(() => CampConfigurationRules.ValidateAnswers(config, new Dictionary<string, string>()));
        Assert.Empty(CampConfigurationRules.ValidateAnswers(config, null, false));
        foreach (var patch in new[] { ("arrival", "forged"), ("stay", "yes"), ("total", "1"), ("consent", "false"), ("comment", new string('a', 2001)) })
        {
            var answers = Answers(); answers[patch.Item1] = patch.Item2;
            Assert.Throws<ArgumentException>(() => CampConfigurationRules.ValidateAnswers(config, answers));
        }
        Assert.Equal("🎲 Привезу игру\nещё строка", CampConfigurationRules.ValidateAnswers(config, Answers())["comment"]);
        var presented = CampConfigurationRules.PresentAnswers(config, new(Answers: Answers()));
        Assert.Equal("18:00", presented["arrival"]); Assert.Equal("Да", presented["stay"]);
    }

    [Fact]
    public void ConfigurationRejectsUnsafeLinksMalformedFieldsAndUnboundedMoney()
    {
        var config = Configuration();
        foreach (var url in new[] { "javascript:alert(1)", "http://example.test", "https://user:secret@example.test" })
            Assert.Throws<ArgumentException>(() => CampConfigurationRules.Normalize(config with { LocationUrl = url }));
        foreach (var money in new[] { -1m, 0.001m, 10000001m })
            Assert.Throws<ArgumentException>(() => CampConfigurationRules.Normalize(config with { Pricing = new(Amount: money) }));
        Assert.Throws<ArgumentException>(() => CampConfigurationRules.Normalize(config with { RegistrationFields = [new("x", "x", "Number")] }));
        Assert.Throws<ArgumentException>(() => CampConfigurationRules.Normalize(config with { RegistrationFields = [new("x", "x", "Choice", Options: [new("a", "A"), new("b", " a ")])] }));
        Assert.Throws<ArgumentException>(() => CampConfigurationRules.Normalize(config with { Pricing = null }));
        Assert.Throws<ArgumentException>(() => CampConfigurationRules.Normalize(config with { Pricing = new(EarlyAmount: 500) }));
        Assert.Throws<ArgumentException>(() => CampConfigurationRules.Normalize(config with { RegistrationFields = Enumerable.Repeat(new CampRegistrationField("x", "x", "Text"), 21).ToArray() }));
        var roundtrip = CampConfigurationRules.Read(CampConfigurationRules.Serialize(config));
        Assert.Equal(CampConfigurationRules.Serialize(config), CampConfigurationRules.Serialize(roundtrip));
    }

    [Fact]
    public async Task SavePersistsAnswersAndQuoteAndNameEditsKeepEarlyPriceAcrossDeadline()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
        var clock = new Clock();
        var camp = new Camp { Name = "Хэллоуин", BotChat = new() { Key = "camp", Mode = BotMode.Camp, Name = "Хэллоуин", TimeZoneId = "Asia/Qyzylorda", TelegramChatId = -10012, IsActive = true },
            Status = CampStatus.Active, StartsAtUtc = DateTimeOffset.Parse("2026-10-31T07:00:00Z"), EndsAtUtc = DateTimeOffset.Parse("2026-11-01T18:00:00Z"), ConfigurationJson = CampConfigurationRules.Serialize(Configuration()) };
        var player = new Participant { TelegramUserId = 42, DisplayName = "Игрок" }; db.Camps.Add(camp); db.Participants.Add(player); await db.SaveChangesAsync();
        var service = new CampRegistrationService(db, clock); DateOnly[] dates = [new(2026, 10, 31), new(2026, 11, 1)];
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveAsync(camp.Id, player.Id, dates, false, "Игрок", "Алматы", false, default));
        Assert.Empty(await db.CampRegistrations.ToArrayAsync());
        await service.SaveAsync(camp.Id, player.Id, dates, false, "Игрок", "Алматы", false, default, Answers());
        var reg = await db.CampRegistrations.Include(x => x.SelectedDays).SingleAsync();
        Assert.True(CampParticipationPolicy.IsRegistrationComplete(reg, camp));
        var saved = CampConfigurationRules.ReadRegistration(reg.RegistrationDataJson); Assert.Equal(11000, saved.Quote!.Total);
        clock.Now = clock.Now.AddDays(1);
        camp.ConfigurationJson = CampConfigurationRules.Serialize(Configuration() with { Pricing = new(Amount: 20000) }); await db.SaveChangesAsync();
        await service.SaveAsync(camp.Id, player.Id, dates, false, "Новое имя", "Астана", false, default); // Legacy client omits answers.
        Assert.Equal(saved.Quote.CalculatedAtUtc, CampConfigurationRules.ReadRegistration(reg.RegistrationDataJson).Quote!.CalculatedAtUtc);
        Assert.Equal(11000, CampConfigurationRules.ReadRegistration(reg.RegistrationDataJson).Quote!.Total);
        await service.SaveAsync(camp.Id, player.Id, [dates[0]], false, "Новое имя", "Астана", false, default, Answers());
        Assert.Equal(22500, CampConfigurationRules.ReadRegistration(reg.RegistrationDataJson).Quote!.Total);
        var manager = new ManagedCommunityService(db, null!, clock);
        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.UpdateCampAsync(camp.Id,
            new(camp.Name, camp.BotChat.TimeZoneId, camp.StartsAtUtc!.Value, camp.EndsAtUtc!.Value, Configuration() with { RegistrationFields = [] }), default));
        await manager.UpdateCampAsync(camp.Id, new(camp.Name, camp.BotChat.TimeZoneId, camp.StartsAtUtc!.Value, camp.EndsAtUtc!.Value,
            Configuration() with { Description = "Обновлённое описание" }), default);
        Assert.Equal(22500, CampConfigurationRules.ReadRegistration(reg.RegistrationDataJson).Quote!.Total);
    }

    internal sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.Parse("2026-10-27T18:59:59Z");
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
