namespace oyinQ.Bot.Data.Entities;

public sealed class GameGatheringExpansion
{
    public long Id { get; set; }
    public long GameGatheringId { get; set; }
    public long BggId { get; set; }
    public string Name { get; set; } = string.Empty;

    public GameGathering GameGathering { get; set; } = null!;
}
