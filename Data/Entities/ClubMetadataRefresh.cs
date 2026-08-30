namespace oyinQ.Bot.Data.Entities;

public sealed class ClubMetadataRefresh
{
    public long Id { get; set; }
    public Guid PublicId { get; set; }
    public long ClubId { get; set; }
    public ClubMetadataRefreshStatus Status { get; set; }
    public string BggIdsJson { get; set; } = "[]";
    public int ProgressCurrent { get; set; }
    public int ProgressTotal { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Club Club { get; set; } = null!;
}
