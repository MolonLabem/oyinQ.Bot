using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Features.Communities;

namespace oyinQ.Bot.Tests;

public sealed partial class PostgreSqlStabilizationTests
{
    [PostgreSqlFact]
    public async Task CampConfigurationMigrationPreservesPopulatedOldRegistrationsAndSurvivesRestart()
    {
        await using var database = await Database.CreateAsync("20261001090000_GatheringParticipantDisplayNames");
        await using (var db = database.Open())
        {
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO "Participants" ("Id","TelegramUserId","DisplayName","CreatedAt","UpdatedAt") VALUES (123,8123,'Участник',now(),now());
                INSERT INTO "OyinQCommunities" ("Key","Name","TelegramChatId","Mode","TimeZoneId","IsActive","CreatedAt","UpdatedAt")
                VALUES ('old-camp','Старый кэмп',-10098123,1,'UTC',true,now(),now());
                INSERT INTO "Camps" ("Id","BotChatKey","BotChatMode","Name","Status","BaseCollectionJson","StartsAtUtc","EndsAtUtc","CreatedByTelegramUserId","CreatedAt","UpdatedAt")
                VALUES (123,'old-camp',1,'Старый кэмп',1,'{{"version":2,"games":[]}}',now()-interval '1 day',now()+interval '1 day',8123,now(),now());
                INSERT INTO "CampRegistrations" ("Id","CampId","ParticipantId","DaysStaying","NeedsAccommodation","City","CreatedAt","UpdatedAt")
                VALUES (123,123,123,1,false,'Алматы',now(),now());
                INSERT INTO "CampRegistrationDays" ("CampRegistrationId","Date") VALUES (123,current_date);
                """);
            await db.Database.MigrateAsync(); await db.Database.MigrateAsync();
            var camp = await db.Camps.Include(x => x.BotChat).SingleAsync();
            var reg = await db.CampRegistrations.Include(x => x.SelectedDays).SingleAsync();
            Assert.Empty(CampConfigurationRules.Read(camp.ConfigurationJson).RegistrationFields!);
            Assert.Empty(CampConfigurationRules.ReadRegistration(reg.RegistrationDataJson).Answers!);
            Assert.True(CampParticipationPolicy.IsRegistrationComplete(reg, camp));
            Assert.Equal("Алматы", reg.City); Assert.False(reg.NeedsAccommodation); Assert.Single(reg.SelectedDays);
            camp.ConfigurationJson = CampConfigurationRules.Serialize(CampConfigurationTests.Configuration());
            reg.RegistrationDataJson = CampConfigurationRules.Serialize(new CampRegistrationData(Answers: CampConfigurationTests.Answers(),
                Quote: CampConfigurationRules.Quote(CampConfigurationTests.Configuration(), CampConfigurationTests.Answers(), 1, "UTC", Now)));
            await db.SaveChangesAsync();
        }
        await using var restarted = database.Open(); await restarted.Database.MigrateAsync();
        Assert.Equal("🎃 Хэллоуин\nИгры и костюмы", CampConfigurationRules.Read((await restarted.Camps.SingleAsync()).ConfigurationJson).Description);
        var data = CampConfigurationRules.ReadRegistration((await restarted.CampRegistrations.SingleAsync()).RegistrationDataJson);
        Assert.Equal("evening", data.Answers!["arrival"]); Assert.NotNull(data.Quote); Assert.False(restarted.Database.HasPendingModelChanges());
    }
}
