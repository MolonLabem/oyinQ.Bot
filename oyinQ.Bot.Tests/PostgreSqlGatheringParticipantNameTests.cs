using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Communities;
using oyinQ.Bot.Features.Gatherings;

namespace oyinQ.Bot.Tests;

public sealed partial class PostgreSqlStabilizationTests
{
    [PostgreSqlFact]
    public async Task ParticipantNameMigrationKeepsExistingRosterAndNullFallback()
    {
        await using var database = await Database.CreateAsync("20260924101113_CampVisibilityDefaults");
        var organizer = await SeedAsync(database);
        var id = Guid.NewGuid(); var snapshot = GatheringGameSnapshotSerializer.Serialize(Snapshot());
        await using (var db = database.Open())
        {
            var playerId = Assert.Single(await db.Database.SqlQuery<long>($"INSERT INTO \"Participants\" (\"TelegramUserId\", \"DisplayName\", \"CreatedAt\", \"UpdatedAt\") VALUES (23456, 'Виктор', {Now}, {Now}) RETURNING \"Id\" AS \"Value\"").ToArrayAsync());
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "GameGatherings" ("Id","PublicId","CommunityKey","GameSnapshotJson","OrganizerParticipantId","StartsAtUtc",
                    "MinimumPlayers","DesiredPlayers","MaximumPlayers","CanTeachRules","Status","PublicationStatus","PublicationAttempts",
                    "PublicationRevision","OutcomeRevision","CreatedAt","UpdatedAt")
                VALUES (100,{id},'club',{snapshot}::jsonb,{organizer.Id},{Now.AddHours(1)},1,2,4,false,0,0,0,0,0,{Now},{Now});
                INSERT INTO "GameGatheringParticipants" ("GameGatheringId","ParticipantId","Status","AttendanceOutcome","JoinedAt")
                VALUES (100,{playerId},0,0,{Now});
                """);
            await db.Database.MigrateAsync();
            await db.Database.MigrateAsync();
        }
        await using var restarted = database.Open();
        var loaded = await new GatheringPlayService(restarted, Time).Gatherings.SingleAsync(x => x.PublicId == id);
        Assert.Null(loaded.OrganizerDisplayNameOverride);
        var member = Assert.Single(loaded.Participants);
        Assert.Null(member.DisplayNameOverride);
        Assert.Equal("Виктор", GatheringParticipantNames.GetDisplayName(member));
        Assert.Equal(organizer.Id, loaded.OrganizerParticipantId);
        Assert.Contains("Виктор", new GatheringPresentationService().BuildTelegramAnnouncement(loaded, loaded.Community.ToBotCommunity()).HtmlText);
    }

    [PostgreSqlFact]
    public async Task ParticipantNamesPersistAcrossContextsWithoutAffectingJoinLeaveWaitlistOrNotifications()
    {
        await using var database = await Database.CreateAsync();
        var organizer = await SeedAsync(database); Guid id; Guid playerPublicId; long playerId;
        await using (var db = database.Open())
        {
            var g = await Management(db, new Bgg()).CreateAsync((await new CommunityStore(db).FindByKeyAsync("club", default))!,
                new(organizer.TelegramUserId, null, "Игрок"), Command() with { MinimumPlayers = 1, DesiredPlayers = 2, MaximumPlayers = 2 }, default);
            id = g.PublicId;
            db.Participants.AddRange(new Participant { TelegramUserId = 23456, DisplayName = "Виктор" },
                new Participant { TelegramUserId = 34567, DisplayName = "Антон" });
            await db.SaveChangesAsync();
            var service = new GatheringService(db, new(db, Time));
            await service.JoinAsync(id, "club", 23456, Now, default);
            await service.JoinAsync(id, "club", 34567, Now, default);
            var player = g.Participants.Single(x => x.Participant.TelegramUserId == 23456).Participant;
            playerId = player.Id; playerPublicId = player.PublicId;
            var notices = await db.Notifications.CountAsync();
            g.TelegramChatId = -10012345; g.TelegramMessageId = 777; await db.SaveChangesAsync();
            await Management(db, new Bgg()).UpdateAsync(id, "club", organizer.TelegramUserId,
                new(g.StartsAtUtc, 1, 2, 2, null, false, [], ParticipantNames: [new(player.PublicId, "Виктор + жена")]), default);
            Assert.Equal(notices, await db.Notifications.CountAsync());
        }
        await using (var db = database.Open())
        {
            var g = await new GatheringPlayService(db, Time).Gatherings.SingleAsync(x => x.PublicId == id);
            Assert.Equal("Виктор + жена", g.Participants.Single(x => x.ParticipantId == playerId).DisplayNameOverride);
            Assert.Equal(777, g.TelegramMessageId);
            Assert.Equal(GatheringPublicationStatus.Pending, g.PublicationStatus);
            var service = new GatheringService(db, new(db, Time));
            await service.LeaveAsync(id, "club", 23456, Now, default);
            Assert.Equal(GatheringParticipationStatus.Confirmed, g.Participants.Single(x => x.Participant.TelegramUserId == 34567).Status);
            await service.JoinAsync(id, "club", 23456, Now, default);
            var member = g.Participants.Single(x => x.ParticipantId == playerId);
            Assert.Equal(GatheringParticipationStatus.Waitlisted, member.Status);
            Assert.Equal("Виктор + жена", GatheringParticipantNames.GetDisplayName(member));
            member.Participant.PreferredDisplayName = "Виктор (профиль)"; await db.SaveChangesAsync();
        }
        await using (var db = database.Open())
        {
            var g = await new GatheringPlayService(db, Time).Gatherings.SingleAsync(x => x.PublicId == id);
            Assert.Equal("Виктор + жена", GatheringParticipantNames.GetDisplayName(g.Participants.Single(x => x.ParticipantId == playerId)));
            await Management(db, new Bgg()).UpdateAsync(id, "club", organizer.TelegramUserId,
                new(g.StartsAtUtc, 1, 2, 2, null, false, [], ParticipantNames: [new(playerPublicId, null)]), default);
            Assert.Equal("Виктор (профиль)", GatheringParticipantNames.GetDisplayName(g.Participants.Single(x => x.ParticipantId == playerId)));
        }
    }
}
