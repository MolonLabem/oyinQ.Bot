namespace oyinQ.Bot.Data.Entities;

public sealed class ClubBggImport
{
    public long Id { get; set; }
    public Guid PublicId { get; set; }
    public long ClubId { get; set; }
    public string BggUsername { get; set; } = string.Empty;
    public ClubBggImportStatus Status { get; set; }
    public int ProgressCurrent { get; set; }
    public int ProgressTotal { get; set; }
    public int AddedGames { get; set; }
    public int AddedExpansions { get; set; }
    public int OrphanExpansions { get; set; }
    public string? Error { get; set; }
    public Guid? LeaseId { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Club Club { get; set; } = null!;
}
