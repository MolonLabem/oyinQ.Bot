using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data;
using oyinQ.Bot.Features.Communities;

namespace oyinQ.Bot.Tests;

public sealed class ManagedCommunityAuthorizationTests
{
    [Fact]
    public async Task SuperAdminCreation_SkipsRequesterMembershipButStillUsesManagedChatValidation()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var dbContext = new AppDbContext(options);
        var validator = new RecordingValidator();
        var service = new ManagedCommunityService(dbContext, validator, TimeProvider.System);

        var club = await service.CreateClubAsync(new CreateClubCommand(
            "Observed club", -100123, "Asia/Qyzylorda", 139527837,
            RequireCreatorTelegramAdmin: false), default);

        Assert.Equal(-100123, club.BotChat.TelegramChatId);
        Assert.False(validator.RequireRequesterAdmin);
        Assert.Equal(139527837, validator.RequesterId);
    }

    private sealed class RecordingValidator : IManagedChatValidator
    {
        public long RequesterId { get; private set; }
        public bool RequireRequesterAdmin { get; private set; }

        public Task<ManagedChatValidation> ValidateAsync(long telegramChatId,
            long requestingAdministratorId, bool requireRequestingAdministrator,
            CancellationToken cancellationToken)
        {
            RequesterId = requestingAdministratorId;
            RequireRequesterAdmin = requireRequestingAdministrator;
            return Task.FromResult(new ManagedChatValidation(true, "Observed club", null, null));
        }
    }
}
