using System.Security.Cryptography;
using System.Text.Json;

namespace oyinQ.Bot.Features.Gatherings;

internal static class GatheringCreationIdentity
{
    public static string Hash(CreateGatheringCommand command)
    {
        // Conflict consent changes during a retry; it is not part of the requested gathering.
        var normalized = command with
        {
            OperationId = null, ConfirmScheduleConflict = false,
            StartsAt = command.StartsAt.ToUniversalTime(),
            GameSource = command.GameSource.ToLowerInvariant(),
            Description = GatheringRules.NormalizeDescription(command.Description),
            SelectedExpansionIds = command.SelectedExpansionIds.Distinct().Order().ToArray(),
            AddExpansionToCollectionIds = command.AddExpansionToCollectionIds?.Distinct().Order().ToArray(),
            BringExpansionIds = command.BringExpansionIds?.Distinct().Order().ToArray()
        };
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(normalized)));
    }
}
