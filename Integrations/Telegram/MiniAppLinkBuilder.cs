using Microsoft.Extensions.Options;
using oyinQ.Bot.Common.Options;

namespace oyinQ.Bot.Integrations.Telegram;

public sealed class MiniAppLinkBuilder(IOptions<BotOptions> options)
{
    private readonly string baseUrl = options.Value.PublicBaseUrl.TrimEnd('/');

    public string FromStartContext(MiniAppStartContext context) => context.GatheringPublicId is { } gathering
        ? Gathering(context.CommunityKey, gathering) : context.ImportPublicId is { } import
            ? CampImport(context.CommunityKey, import) : context.CollectionBggId is { } bgg
                ? CollectionGame(context.CommunityKey, bgg) : Community(context.CommunityKey);
    public string App() => $"{baseUrl}/app/";
    public string Admin() => $"{App()}?admin=1";
    public string ProfileCollection(Guid importId) => $"{App()}?tab=profile&profileImport={importId}";
    public string Privacy() => $"{baseUrl}/privacy";
    public string Community(string communityKey) =>
        $"{App()}?community={Uri.EscapeDataString(communityKey)}";
    public string Gathering(string communityKey, Guid publicId) =>
        $"{Community(communityKey)}&gathering={publicId}";
    public string CollectionGame(string communityKey, long bggId) =>
        $"{Community(communityKey)}&tab=games&game={bggId}";
    public string CampImport(string communityKey, Guid importId) =>
        $"{Community(communityKey)}&tab=mine&import={importId}";
}
