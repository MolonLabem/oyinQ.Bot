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
    IReadOnlyList<ClubCollectionExpansion>? KnownExpansions = null,
    string? Description = null,
    int? YearPublished = null,
    int? MinPlayTimeMinutes = null,
    int? MaxPlayTimeMinutes = null,
    int? MinAge = null,
    GameType Type = GameType.Other,
    IReadOnlyList<GameTaxonomyItem>? Categories = null,
    IReadOnlyList<GameTaxonomyItem>? Mechanics = null,
    bool PlayerRangeDefaulted = false,
    string? OriginalName = null)
{
    public const int CurrentVersion = 3;

    public static GatheringGameSnapshot FromClubGame(
        ClubCollectionGame game,
        IReadOnlyCollection<long> selectedExpansionIds,
        string source = "catalog")
    {
        var players = PlayerCountRange.Normalize(game.MinPlayers, game.MaxPlayers);
        return new(
            CurrentVersion,
            game.BggId,
            game.Name,
            game.ThumbnailImageUrl,
            game.ImageUrl,
            players.Minimum,
            players.Maximum,
            game.BestPlayers,
            GatheringExpansionSelection.Select(game.Expansions, selectedExpansionIds),
            source,
            game.Expansions.ToArray(),
            game.Description,
            game.YearPublished,
            game.MinPlayTimeMinutes,
            game.MaxPlayTimeMinutes,
            game.MinAge,
            BggTaxonomyCatalog.ResolveType(game.Type, game.Subdomains, game.Types,
                game.CategoryItems, game.Categories),
            game.CategoryItems,
            game.Mechanics,
            players.WasDefaulted,
            game.OriginalName);
    }
}

public static class GatheringExpansionSelection
{
    public static IReadOnlyList<ClubCollectionExpansion> Select(IReadOnlyCollection<ClubCollectionExpansion> available, IReadOnlyCollection<long> selected)
    {
        var known = available.Select(x => x.BggId).ToHashSet();
        if (selected.Any(id => !known.Contains(id)))
            throw new InvalidOperationException("Выбрано дополнение, которое не относится к этой игре.");
        return available.Where(x => selected.Contains(x.BggId)).DistinctBy(x => x.BggId).ToArray();
    }
}

public static class GatheringGameSnapshotSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(GatheringGameSnapshot snapshot)
    {
        snapshot = Normalize(snapshot);
        Validate(snapshot);
        return JsonSerializer.Serialize(snapshot, Options);
    }

    public static GatheringGameSnapshot Deserialize(string? json)
    {
        try
        {
            var snapshot = JsonSerializer.Deserialize<GatheringGameSnapshot>(json ?? string.Empty, Options)
                ?? throw new InvalidOperationException("Снимок игры сбора пуст.");
            snapshot = Normalize(snapshot);
            Validate(snapshot);
            return snapshot;
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("Снимок игры сбора содержит некорректный JSON.", exception);
        }
    }

    private static GatheringGameSnapshot Normalize(GatheringGameSnapshot snapshot)
    {
        var players = PlayerCountRange.Normalize(snapshot.MinPlayers, snapshot.MaxPlayers);
        return snapshot with
        {
            SelectedExpansions = snapshot.SelectedExpansions ?? [],
            MinPlayers = players.Minimum,
            MaxPlayers = players.Maximum,
            PlayerRangeDefaulted = snapshot.PlayerRangeDefaulted || players.WasDefaulted
        };
    }

    public static void Validate(GatheringGameSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Version is not 1 and not 2 and not GatheringGameSnapshot.CurrentVersion)
        {
            throw new InvalidOperationException($"Неподдерживаемая версия снимка игры сбора: {snapshot.Version}.");
        }

        if (string.IsNullOrWhiteSpace(snapshot.Name) || snapshot.OriginalName?.Length > 300)
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
