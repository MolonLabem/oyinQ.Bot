using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Features.Collections;

namespace oyinQ.Bot.Data.Entities;

public sealed class Club
{
    public long Id { get; set; }
    public string BotChatKey { get; set; } = string.Empty;
    public BotMode BotChatMode { get; private set; } = BotMode.Club;
    public string Name { get; set; } = string.Empty;
    public string? BggUsername { get; set; }
    public string CollectionJson { get; set; } = ClubCollectionSerializer.Serialize(ClubCollectionDocument.Empty);
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public OyinQCommunity BotChat { get; set; } = null!;
    public ICollection<Camp> SourceCamps { get; set; } = [];

    public ClubCollectionDocument ReadCollection() => ClubCollectionSerializer.Deserialize(CollectionJson);

    public void ReplaceCollection(ClubCollectionDocument document, DateTimeOffset now)
    {
        CollectionJson = ClubCollectionSerializer.Serialize(document);
        UpdatedAt = now.ToUniversalTime();
    }
}
