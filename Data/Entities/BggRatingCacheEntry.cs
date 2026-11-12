namespace oyinQ.Bot.Data.Entities;

// Public provider metadata only. Collection membership and personal data stay in their existing stores.
public sealed class BggRatingCacheEntry
{
    public long BggId { get; set; }
    public decimal? Rating { get; set; }
    public DateTimeOffset? CheckedAt { get; set; }
    public DateTimeOffset RefreshAfter { get; set; }
    public bool Failed { get; set; }
    public bool RefreshRequested { get; set; }
    public Guid? LeaseId { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }
}
