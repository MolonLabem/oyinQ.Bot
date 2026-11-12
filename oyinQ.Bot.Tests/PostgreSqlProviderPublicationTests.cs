using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Catalog;
using oyinQ.Bot.Features.Collections;
using oyinQ.Bot.Features.Communities;
using oyinQ.Bot.Features.Gatherings;

namespace oyinQ.Bot.Tests;

public sealed partial class PostgreSqlStabilizationTests
{
    [PostgreSqlFact]
    public async Task CampBoxMutationsRefreshExistingAnnouncementsAndPreservePublicationLeases()
    {
        await using var database = await Database.CreateAsync();
        var actor = await SeedAsync(database, camp: true);
        await using var db = database.Open();
        var camp = await db.Camps.SingleAsync();
        var other = new Participant { TelegramUserId = 56789, DisplayName = "Организатор" };
        db.Participants.Add(other);
        await db.SaveChangesAsync();
        await new ParticipantCollectionService(db).UpsertAsync(actor.Id, [Item()], CollectionItemSource.Manual, Now, default);
        var states = new[] { GatheringPublicationStatus.Published, GatheringPublicationStatus.Preparing,
            GatheringPublicationStatus.Delivering, GatheringPublicationStatus.DeliveryUnknown, GatheringPublicationStatus.NotRequired };
        var gatherings = states.Select(state =>
        {
            var g = GatheringRules.Create("club", Snapshot(), other.Id, Now.AddHours(1), 1, 2, 4, null, false, Now);
            g.PublicationStatus = state; g.TelegramChatId = -10012345; g.TelegramMessageId = 100 + (int)state;
            g.PublicationRevision = 10;
            return g;
        }).ToArray();
        var foreignGame = GatheringRules.Create("club", Snapshot() with { BggId = 43 }, other.Id, Now.AddHours(1), 1, 2, 4, null, false, Now);
        var past = GatheringRules.Create("club", Snapshot(), other.Id, Now.AddHours(1), 1, 2, 4, null, false, Now);
        past.StartsAtUtc = Now.AddHours(-1);
        var cancelled = GatheringRules.Create("club", Snapshot(), other.Id, Now.AddHours(1), 1, 2, 4, null, false, Now);
        cancelled.Status = GatheringStatus.Cancelled;
        db.GameGatherings.AddRange(gatherings); db.GameGatherings.AddRange(foreignGame, past, cancelled);
        await db.SaveChangesAsync();
        var untouched = new[] { foreignGame, past, cancelled }.Select(g => (g.Id, g.PublicationRevision)).ToArray();
        var policy = new CampParticipationPolicy(db, Time);
        var contributions = new CampContributionSelectionService(db, policy, Time);
        var effective = new EffectiveCampCatalogService(db, contributions);
        var providers = new GameProviderService(db, new(db, effective, Time), effective, policy, contributions, Time);
        var g = gatherings[0];
        Assert.NotNull((await providers.ForGatheringAsync(g, actor.Id, default)).Warning);
        await contributions.SetCommitmentAsync(camp.Id, actor.Id, 42, CollectionItemType.BaseGame, CampBringCommitment.Bringing, default);
        Assert.Null((await providers.ForGatheringAsync(g, actor.Id, default)).Warning);
        Assert.Equal(GatheringPublicationStatus.Pending, g.PublicationStatus);
        for (var i = 0; i < states.Length; i++)
        {
            Assert.Equal(i == 0 ? GatheringPublicationStatus.Pending : states[i], gatherings[i].PublicationStatus);
            Assert.Equal(states[i] == GatheringPublicationStatus.NotRequired ? 10 : 11, gatherings[i].PublicationRevision);
        }
        foreach (var (id, revision) in untouched)
            Assert.Equal(revision, await db.GameGatherings.Where(x => x.Id == id).Select(x => x.PublicationRevision).SingleAsync());

        async Task AssertQueuedAfter(Func<Task> mutate, bool confirmed)
        {
            g.PublicationStatus = GatheringPublicationStatus.Published;
            await db.SaveChangesAsync();
            var revision = g.PublicationRevision;
            await mutate();
            Assert.Equal(GatheringPublicationStatus.Pending, g.PublicationStatus);
            Assert.True(g.PublicationRevision > revision);
            Assert.Equal(confirmed, (await providers.ForGatheringAsync(g, actor.Id, default)).IsConfirmed);
            // The caller can keep using tracked rows without reverting an atomic invalidation.
            g.Description = "Обновлённый текст";
            await db.SaveChangesAsync();
            Assert.Equal(g.PublicationRevision, await db.GameGatherings.AsNoTracking().Where(x => x.Id == g.Id)
                .Select(x => x.PublicationRevision).SingleAsync());
        }
        await AssertQueuedAfter(() => contributions.SetCommitmentAsync(camp.Id, actor.Id, 42,
            CollectionItemType.BaseGame, CampBringCommitment.Available, default), false);
        await AssertQueuedAfter(() => contributions.SetCommitmentAsync(camp.Id, actor.Id, 42,
            CollectionItemType.BaseGame, CampBringCommitment.Bringing, default), true);
        var today = DateOnly.FromDateTime(Now.UtcDateTime);
        var registration = new CampRegistrationService(db, Time);
        await AssertQueuedAfter(() => registration.SaveAsync(camp.Id, actor.Id, [today.AddDays(-1), today], false,
            null, "Алматы", true, default), true);
        await AssertQueuedAfter(() => contributions.SetCommitmentAsync(camp.Id, actor.Id, 42,
            CollectionItemType.BaseGame, CampBringCommitment.Bringing, default, availableDates: [today.AddDays(-1)]), false);
        await AssertQueuedAfter(() => contributions.SetCommitmentAsync(camp.Id, actor.Id, 42,
            CollectionItemType.BaseGame, CampBringCommitment.Bringing, default, allAttendanceDays: true), true);
        await AssertQueuedAfter(() => contributions.RemoveAsync(camp.Id, actor.Id, 42, CollectionItemType.BaseGame, default), false);
        await AssertQueuedAfter(() => contributions.SetCommitmentAsync(camp.Id, actor.Id, 42,
            CollectionItemType.BaseGame, CampBringCommitment.Bringing, default), true);
        var imported = await db.CampGameContributions.SingleAsync();
        imported.Source = CollectionItemSource.BggImport;
        var import = new CampBggImport { PublicId = Guid.NewGuid(), CampId = camp.Id, ParticipantId = actor.Id,
            BggUsername = "owner", Status = CampBggImportStatus.Completed, ExpiresAt = Now.AddDays(1),
            DraftJson = CampBggImportDraftSerializer.Serialize(new(3, "owner", [Item()])) };
        db.CampBggImports.Add(import); await db.SaveChangesAsync();
        await AssertQueuedAfter(() => Imports(db).ConfirmAsync(import.PublicId, camp.Id, actor.Id, [], [], default), false);
        await AssertQueuedAfter(() => contributions.SetCommitmentAsync(camp.Id, actor.Id, 42,
            CollectionItemType.BaseGame, CampBringCommitment.Bringing, default), true);
        await AssertQueuedAfter(() => registration.SaveAsync(camp.Id, actor.Id, [today.AddDays(-1)], false,
            null, "Алматы", true, default), false);
        await AssertQueuedAfter(() => registration.SaveAsync(camp.Id, actor.Id, [today], false,
            null, "Алматы", true, default), true);
        await AssertQueuedAfter(() => registration.UnregisterAsync(camp.Id, actor.Id, default), false);
        Assert.Equal(GatheringStatus.Ready, g.Status);
        Assert.Equal(101, g.TelegramMessageId);
    }

    [PostgreSqlFact]
    public async Task DeletingProviderProfileQueuesOtherOrganizersCampAnnouncements()
    {
        await using var database = await Database.CreateAsync();
        var actor = await SeedAsync(database, camp: true);
        await using var db = database.Open();
        var camp = await db.Camps.SingleAsync();
        var other = new Participant { TelegramUserId = 87654, DisplayName = "Организатор" };
        db.Participants.Add(other); await db.SaveChangesAsync();
        var g = GatheringRules.Create("club", Snapshot(), other.Id, Now.AddHours(1), 1, 2, 4, null, false, Now);
        g.PublicationStatus = GatheringPublicationStatus.Published; g.TelegramMessageId = 123;
        db.GameGatherings.Add(g);
        db.CampGameContributions.Add(new() { CampId = camp.Id, ParticipantId = actor.Id, BggId = 42,
            ItemType = CollectionItemType.BaseGame, Commitment = CampBringCommitment.Bringing, SnapshotJson = "{}" });
        await db.SaveChangesAsync();
        var revision = g.PublicationRevision;
        await ProfileDeletion(db).DeleteAsync(actor.TelegramUserId, default);
        Assert.Equal(GatheringPublicationStatus.Pending, g.PublicationStatus);
        Assert.Equal(revision + 1, g.PublicationRevision);
        Assert.Equal(123, g.TelegramMessageId);
        Assert.Equal(GatheringStatus.Ready, g.Status);
    }
}
