using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Integrations.Telegram;

namespace oyinQ.Bot.Tests;

public sealed class TelegramPeerSelectionRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 29, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AdminButton_RequestsOnlyRegularUsersAndIdentityFields()
    {
        var button = TelegramPeerSelectionRules.CreateButton(TelegramPeerSelectionPurpose.AddAdministrator, 42);
        Assert.NotNull(button.RequestUsers);
        Assert.False(button.RequestUsers.UserIsBot);
        Assert.Equal(10, button.RequestUsers.MaxQuantity);
        Assert.True(button.RequestUsers.RequestName);
        Assert.True(button.RequestUsers.RequestUsername);
        Assert.True(button.RequestUsers.RequestPhoto);
        Assert.Null(button.RequestChat);
    }

    [Theory]
    [InlineData(TelegramPeerSelectionPurpose.CreateClubChat)]
    [InlineData(TelegramPeerSelectionPurpose.CreateCampChat)]
    public void ManagedChatButton_RequestsGroupWithBotMembership(TelegramPeerSelectionPurpose purpose)
    {
        var request = TelegramPeerSelectionRules.CreateButton(purpose, 51).RequestChat!;
        Assert.False(request.ChatIsChannel);
        Assert.True(request.BotIsMember);
        Assert.True(request.RequestTitle);
    }

    [Fact]
    public void Request_BelongsToInitiatingAdministrator()
    {
        var pending = Pending();
        Assert.Equal(PeerSelectionDecision.WrongOwner,
            TelegramPeerSelectionRules.Evaluate(pending, 8, pending.Purpose, Now));
    }

    [Fact]
    public void Request_RejectsWrongPurposeAndExpiry()
    {
        var pending = Pending();
        Assert.Equal(PeerSelectionDecision.WrongPurpose,
            TelegramPeerSelectionRules.Evaluate(pending, 7, TelegramPeerSelectionPurpose.CreateCampChat, Now));
        pending.ExpiresAt = Now;
        Assert.Equal(PeerSelectionDecision.Expired,
            TelegramPeerSelectionRules.Evaluate(pending, 7, pending.Purpose, Now));
    }

    [Theory]
    [InlineData(TelegramPeerSelectionStatus.Completed)]
    [InlineData(TelegramPeerSelectionStatus.Consumed)]
    public void Replay_IsHarmless(TelegramPeerSelectionStatus status)
    {
        var pending = Pending(); pending.Status = status;
        Assert.Equal(PeerSelectionDecision.Replay,
            TelegramPeerSelectionRules.Evaluate(pending, 7, pending.Purpose, Now));
    }

    private static PendingTelegramPeerSelection Pending() => new()
    {
        RequestedByTelegramUserId = 7,
        Purpose = TelegramPeerSelectionPurpose.CreateClubChat,
        Status = TelegramPeerSelectionStatus.Pending,
        ExpiresAt = Now.AddMinutes(1)
    };
}
