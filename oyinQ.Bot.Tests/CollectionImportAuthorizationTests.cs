using Microsoft.Extensions.Options;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Collections;

namespace oyinQ.Bot.Tests;

public sealed class CollectionImportAuthorizationTests
{
    [Fact]
    public async Task EnqueueClubAsync_NonAdmin_IsRejectedBeforeDatabaseAccess()
    {
        var service = new CollectionImportService(
            null!,
            Options.Create(new CampOptions
            {
                AdminTelegramIds = new HashSet<long> { 42 }
            }),
            Options.Create(new BggOptions()));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => service.EnqueueClubAsync(
                100,
                ExternalGameProvider.Bgg,
                "club",
                CancellationToken.None));
    }
}
