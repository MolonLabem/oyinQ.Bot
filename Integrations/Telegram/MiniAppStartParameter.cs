using Microsoft.AspNetCore.WebUtilities;

namespace oyinQ.Bot.Integrations.Telegram;

public sealed record MiniAppStartContext(string CommunityKey, Guid? GatheringPublicId);

public static class MiniAppStartParameter
{
    public static string ForCommunity(string communityKey) => $"community-{communityKey}";

    public static string ForGathering(string communityKey, Guid publicId) =>
        $"g-{WebEncoders.Base64UrlEncode(publicId.ToByteArray())}-{communityKey}";

    public static MiniAppStartContext? Parse(string? messageText)
    {
        if (string.IsNullOrWhiteSpace(messageText)) return null;
        var parts = messageText.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) return null;

        var parameter = parts[1];
        if (parameter.StartsWith("community-", StringComparison.Ordinal))
        {
            return new MiniAppStartContext(parameter["community-".Length..], null);
        }

        const int separatorIndex = 24;
        if (!parameter.StartsWith("g-", StringComparison.Ordinal)
            || parameter.Length <= separatorIndex + 1
            || parameter[separatorIndex] != '-') return null;

        try
        {
            var bytes = WebEncoders.Base64UrlDecode(parameter[2..separatorIndex]);
            return bytes.Length == 16
                ? new MiniAppStartContext(parameter[(separatorIndex + 1)..], new Guid(bytes))
                : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
