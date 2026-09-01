using oyinQ.Bot.Common.Options;

namespace oyinQ.Bot.Features.Communities;

public interface ICommunityMembershipVerifier
{
    Task<bool> IsMemberAsync(long telegramChatId, long telegramUserId, CancellationToken cancellationToken);
}

public sealed class CommunityContextResolver(
    ICommunityStore communityStore,
    ICommunityMembershipVerifier membershipVerifier)
{
    private async Task<BotCommunity?> ResolveConfiguredByKeyAsync(
        string? communityKey,
        CancellationToken cancellationToken) =>
        string.IsNullOrWhiteSpace(communityKey)
            ? null
            : await communityStore.FindByKeyAsync(communityKey.Trim().ToLowerInvariant(), cancellationToken);

    public async Task<BotCommunity?> ResolveAuthorizedAsync(
        string? communityKey,
        long telegramUserId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(communityKey))
        {
            return null;
        }

        var community = await ResolveConfiguredByKeyAsync(communityKey, cancellationToken);
        if (community is null)
        {
            return null;
        }

        return await membershipVerifier.IsMemberAsync(
            community.TelegramChatId,
            telegramUserId,
            cancellationToken)
            ? community
            : null;
    }

    public async Task<IReadOnlyList<BotCommunity>> ResolveAuthorizedAsync(
        long telegramUserId,
        CancellationToken cancellationToken)
    {
        var authorized = new List<BotCommunity>();
        var communities = await communityStore.ListActiveAsync(cancellationToken);
        foreach (var community in communities)
        {
            if (await membershipVerifier.IsMemberAsync(
                    community.TelegramChatId,
                    telegramUserId,
                    cancellationToken))
            {
                authorized.Add(community);
            }
        }

        return authorized;
    }
}
