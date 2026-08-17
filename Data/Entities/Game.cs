namespace oyinQ.Bot.Data.Entities;

public sealed class Game
{
    public long Id { get; set; }
    public long? BggId { get; set; }
    public string? TeseraAlias { get; set; }
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public int? MinPlayers { get; set; }
    public int? MaxPlayers { get; set; }
    public string? BestPlayers { get; set; }
    public string? ExternalUrl { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<GameCopy> Copies { get; set; } = [];
    public ICollection<GameInterest> Interests { get; set; } = [];
    public ICollection<GameSession> Sessions { get; set; } = [];
}
