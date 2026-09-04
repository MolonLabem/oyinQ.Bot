using Microsoft.AspNetCore.WebUtilities;

namespace oyinQ.Bot.Integrations.Telegram;

public sealed record MiniAppStartContext(string CommunityKey, Guid? GatheringPublicId, long? CollectionBggId = null, Guid? ImportPublicId = null);

public static class MiniAppStartParameter
{
    public static string ForCommunity(string communityKey) => $"community-{communityKey}";

    public static string ForGathering(string communityKey, Guid publicId) =>
        $"g-{WebEncoders.Base64UrlEncode(publicId.ToByteArray())}-{communityKey}";

    public static string ForCampImport(string communityKey, Guid importId) =>
        $"i-{WebEncoders.Base64UrlEncode(importId.ToByteArray())}-{communityKey}";

    public static string ForCollectionGame(string communityKey, long bggId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bggId);
        return $"c-{bggId}-{communityKey}";
    }

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

        if (parameter.StartsWith("c-", StringComparison.Ordinal))
        {
            var separator = parameter.IndexOf('-', 2);
            return separator > 2
                && long.TryParse(parameter.AsSpan(2, separator - 2), out var bggId)
                && bggId > 0
                && separator < parameter.Length - 1
                    ? new MiniAppStartContext(parameter[(separator + 1)..], null, bggId)
                    : null;
        }

        const int separatorIndex = 24;
        if (!(parameter.StartsWith("g-", StringComparison.Ordinal) || parameter.StartsWith("i-", StringComparison.Ordinal))
            || parameter.Length <= separatorIndex + 1
            || parameter[separatorIndex] != '-') return null;

        try
        {
            var bytes = WebEncoders.Base64UrlDecode(parameter[2..separatorIndex]);
            return bytes.Length == 16
                ? parameter.StartsWith("i-", StringComparison.Ordinal)
                    ? new MiniAppStartContext(parameter[(separatorIndex + 1)..], null, ImportPublicId: new Guid(bytes))
                    : new MiniAppStartContext(parameter[(separatorIndex + 1)..], new Guid(bytes))
                : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
