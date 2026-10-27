using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Collections;
using oyinQ.Bot.Features.Communities;
using oyinQ.Bot.Features.Notifications;
using oyinQ.Bot.Integrations;

namespace oyinQ.Bot.Tests;

public sealed partial class PostgreSqlStabilizationTests
{
    [PostgreSqlFact]
    public async Task UncancelledImportCompletesOnceAndWaitsForExplicitConfirmation()
    {
        await using var database = await Database.CreateAsync(); var actor = await SeedAsync(database);
        await using (var db = database.Open()) await Imports(db).QueueAsync(null, actor.Id, "synthetic_collection", default);
        var services = new ServiceCollection();
        services.AddScoped(_ => database.Open());
        services.AddScoped(_ => new CampBggImportService(new Bgg(importGames:
            [new ExternalGame(42, "Игра", 1, 4, null, "https://boardgamegeek.com/boardgame/42")])));
        services.AddScoped(p => new NotificationService(p.GetRequiredService<AppDbContext>(), Time));
        await using var provider = services.BuildServiceProvider();
        var worker = new CampBggImportWorker(provider.GetRequiredService<IServiceScopeFactory>(), Time,
            NullLogger<CampBggImportWorker>.Instance);

        Assert.True(await worker.ProcessOneAsync(default));
        Assert.False(await worker.ProcessOneAsync(default));

        await using var verify = database.Open();
        var import = await verify.CampBggImports.SingleAsync();
        Assert.Equal(CampBggImportStatus.Completed, import.Status); Assert.Equal(BggImportStage.Completed, import.Stage);
        Assert.Equal(42, Assert.Single(CampBggImportDraftSerializer.Deserialize(import.DraftJson).Items).BggId);
        Assert.Null(import.LeaseId); Assert.Null(import.LeaseExpiresAt); Assert.Null(import.CancellationRequestedAt);
        Assert.Single(await verify.Notifications.Where(x => x.Kind == NotificationKind.ImportCompleted).ToArrayAsync());
        Assert.Empty(await verify.ParticipantCollectionItems.ToArrayAsync());
    }

    [PostgreSqlFact]
    public Task ImportCancellationWinsBetweenTheLastReadAndCompletionWrite() =>
        AssertImportCancellationWinsAsync(unregister: false);

    [PostgreSqlFact]
    public Task CampUnregistrationWinsBetweenTheLastReadAndImportCompletionWrite() =>
        AssertImportCancellationWinsAsync(unregister: true);

    private static async Task AssertImportCancellationWinsAsync(bool unregister)
    {
        await using var database = await Database.CreateAsync();
        var actor = await SeedAsync(database, camp: unregister);
        Guid importId; long? campId;
        await using (var db = database.Open())
        {
            campId = unregister ? await db.Camps.Select(x => x.Id).SingleAsync() : null;
            importId = (await Imports(db).QueueAsync(campId, actor.Id, "synthetic_collection", default)).PublicId;
        }
        var entered = Signal(); var resume = Signal();
        var services = new ServiceCollection();
        services.AddScoped(_ => database.Open(new ImportCompletionGate(entered, resume)));
        services.AddScoped(_ => new CampBggImportService(new Bgg(importGames:
            [new ExternalGame(42, "Игра", 1, 4, null, "https://boardgamegeek.com/boardgame/42")])));
        services.AddScoped(p => new NotificationService(p.GetRequiredService<AppDbContext>(), Time));
        await using var provider = services.BuildServiceProvider();
        var worker = new CampBggImportWorker(provider.GetRequiredService<IServiceScopeFactory>(), Time,
            NullLogger<CampBggImportWorker>.Instance);
        var processing = worker.ProcessOneAsync(default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await using var db = database.Open();
            if (unregister) await new CampRegistrationService(db, Time).UnregisterAsync(campId!.Value, actor.Id, default);
            else await Imports(db).CancelAsync(importId, campId, actor.Id, default);
        }
        finally { resume.TrySetResult(); }

        Assert.True(await processing.WaitAsync(TimeSpan.FromSeconds(10)));
        await using var verify = database.Open();
        var import = await verify.CampBggImports.SingleAsync();
        Assert.Equal(CampBggImportStatus.Cancelled, import.Status);
        Assert.Equal(BggImportStage.Cancelled, import.Stage);
        Assert.NotNull(import.CancellationRequestedAt);
        Assert.Null(import.DraftJson); Assert.Null(import.LeaseId); Assert.Null(import.LeaseExpiresAt);
        Assert.Empty(await verify.Notifications.Where(x => x.Kind == NotificationKind.ImportCompleted).ToArrayAsync());
        Assert.Empty(await verify.ParticipantCollectionItems.ToArrayAsync());
        if (unregister) Assert.Empty(await verify.CampRegistrations.ToArrayAsync());
    }
}
