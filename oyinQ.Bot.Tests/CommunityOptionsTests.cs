using Microsoft.Extensions.Configuration;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Features.Communities;

namespace oyinQ.Bot.Tests;

public sealed class CommunityOptionsTests
{
    [Fact]
    public void FromConfiguration_ParsesBothModes()
    {
        var configuration = new ConfigurationManager
        {
            ["OYINQ_COMMUNITIES"] = """
                [
                  { "key": "club", "name": "Клуб", "telegramChatId": -1001, "mode": "Club", "timeZone": "UTC" },
                  { "key": "camp", "name": "Кэмп", "telegramChatId": -1002, "mode": "Camp", "timeZone": "UTC" }
                ]
                """
        };

        var options = CommunityOptions.FromConfiguration(configuration);

        Assert.Equal(BotMode.Club, options.Communities[0].Mode);
        Assert.Equal(BotMode.Camp, options.Communities[1].Mode);
    }

    [Fact]
    public void FromConfiguration_AcceptsLegacyModeAliasesDuringDeploymentTransition()
    {
        var configuration = new ConfigurationManager
        {
            ["OYINQ_COMMUNITIES"] = """
                [{ "key": "club", "name": "Клуб", "telegramChatId": -1001, "mode": "Gatherer", "timeZone": "UTC" }]
                """
        };

        Assert.Equal(BotMode.Club, CommunityOptions.FromConfiguration(configuration).Communities.Single().Mode);
    }

    [Fact]
    public void FromConfiguration_RequiresExplicitCommunities()
    {
        var configuration = new ConfigurationManager
        {
            ["BOARD_CAMP_CHAT_ID"] = "-1007"
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            CommunityOptions.FromConfiguration(configuration));

        Assert.Contains("OYINQ_COMMUNITIES", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Resolver_ResolvesConfiguredChats_AndRejectsUnknownContext()
    {
        BotCommunity[] communities = [
            new("club", "Клуб", -1001, BotMode.Club, "UTC"),
            new("camp", "BoardCamp", -1002, BotMode.Camp, "UTC")
        ];
        var resolver = new CommunityContextResolver(
            new StubCommunityStore(communities),
            new StubMembershipVerifier(true));

        Assert.Equal(BotMode.Club, (await resolver.ResolveByChatIdAsync(-1001, default))?.Mode);
        Assert.Equal(BotMode.Camp, (await resolver.ResolveByChatIdAsync(-1002, default))?.Mode);
        Assert.Null(await resolver.ResolveByChatIdAsync(-9999, default));
        Assert.Null(await resolver.ResolveAuthorizedAsync("forged", 42, default));
        Assert.Equal(2, (await resolver.ResolveAuthorizedAsync(42, default)).Count);
    }

    [Fact]
    public void FromConfiguration_RejectsNonGroupChatId()
    {
        var configuration = new ConfigurationManager
        {
            ["OYINQ_COMMUNITIES"] = """
                [{ "key": "club", "name": "Клуб", "telegramChatId": 1001, "mode": "Gatherer", "timeZone": "UTC" }]
                """
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            CommunityOptions.FromConfiguration(configuration));

        Assert.Contains("negative Telegram", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Resolver_DoesNotTrustKnownFrontendContextWithoutMembership()
    {
        var resolver = new CommunityContextResolver(
            new StubCommunityStore([new BotCommunity("club", "Клуб", -1001, BotMode.Club, "UTC")]),
            new StubMembershipVerifier(false));

        var result = await resolver.ResolveAuthorizedAsync("club", 42, default);

        Assert.Null(result);
    }

    private sealed class StubMembershipVerifier(bool isMember) : ICommunityMembershipVerifier
    {
        public Task<bool> IsMemberAsync(long telegramChatId, long telegramUserId, CancellationToken cancellationToken) =>
            Task.FromResult(isMember);
    }

    private sealed class StubCommunityStore(IReadOnlyList<BotCommunity> communities) : ICommunityStore
    {
        public Task<BotCommunity?> FindByChatIdAsync(long telegramChatId, CancellationToken cancellationToken) =>
            Task.FromResult(communities.SingleOrDefault(value => value.TelegramChatId == telegramChatId));

        public Task<BotCommunity?> FindByKeyAsync(string communityKey, CancellationToken cancellationToken) =>
            Task.FromResult(communities.SingleOrDefault(value => value.Key == communityKey));

        public Task<IReadOnlyList<BotCommunity>> ListActiveAsync(CancellationToken cancellationToken) =>
            Task.FromResult(communities);
    }
}
