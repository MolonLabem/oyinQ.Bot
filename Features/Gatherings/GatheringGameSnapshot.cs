using System.Text.Json;
using oyinQ.Bot.Features.Collections;

namespace oyinQ.Bot.Features.Gatherings;

public sealed record GatheringGameSnapshot(
    int Version,
    long? BggId,
    string Name,
    string? ThumbnailImageUrl,
    string? ImageUrl,
    int? MinPlayers,
    int? MaxPlayers,
    string? BestPlayers,
    IReadOnlyList<ClubCollectionExpansion> SelectedExpansions,
    string Source = "legacy",
    IReadOnlyList<ClubCollectionExpansion>? KnownExpansions = null)
{
    public const int CurrentVersion = 2;

    public static GatheringGameSnapshot FromClubGame(
        ClubCollectionGame game,
        IReadOnlyCollection<long> selectedExpansionIds) =>
        new(
            CurrentVersion,
            game.BggId,
            game.Name,
            game.ThumbnailImageUrl,
            game.ImageUrl,
            game.MinPlayers,
            game.MaxPlayers,
            game.BestPlayers,
            game.Expansions.Where(value => selectedExpansionIds.Contains(value.BggId)).ToArray(),
            "catalog",
            game.Expansions.ToArray());
}

public static class GatheringGameSnapshotSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(GatheringGameSnapshot snapshot)
    {
        Validate(snapshot);
        return JsonSerializer.Serialize(snapshot, Options);
    }

    public static GatheringGameSnapshot Deserialize(string? json)
    {
        try
        {
            var snapshot = JsonSerializer.Deserialize<GatheringGameSnapshot>(json ?? string.Empty, Options)
                ?? throw new InvalidOperationException("Снимок игры сбора пуст.");
            Validate(snapshot);
            return snapshot;
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("Снимок игры сбора содержит некорректный JSON.", exception);
        }
    }

    public static void Validate(GatheringGameSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Version is not 1 and not GatheringGameSnapshot.CurrentVersion)
        {
            throw new InvalidOperationException($"Unsupported gathering game snapshot version {snapshot.Version}.");
        }

        if (string.IsNullOrWhiteSpace(snapshot.Name))
        {
            throw new InvalidOperationException("В снимке игры сбора отсутствует название.");
        }

        if (snapshot.BggId is <= 0)
        {
            throw new InvalidOperationException("BGG ID в снимке игры сбора должен быть положительным.");
        }

        if (snapshot.SelectedExpansions is null
            || snapshot.SelectedExpansions.Any(value => value.BggId <= 0 || string.IsNullOrWhiteSpace(value.Name)))
        {
            throw new InvalidOperationException("Снимок игры сбора содержит некорректные выбранные дополнения.");
        }
        if (snapshot.Version >= 2 && (snapshot.KnownExpansions is null
            || snapshot.KnownExpansions.Any(value => value.BggId <= 0 || string.IsNullOrWhiteSpace(value.Name))))
            throw new InvalidOperationException("Снимок игры сбора содержит некорректный список дополнений.");
    }
}
