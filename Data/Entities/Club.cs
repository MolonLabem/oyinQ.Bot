using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Features.Collections;

namespace oyinQ.Bot.Data.Entities;

public sealed class Club
{
    public long Id { get; set; }
    public string BotChatKey { get; set; } = string.Empty;
    public BotMode BotChatMode { get; private set; } = BotMode.Club;
    public string Name { get; set; } = string.Empty;
    public string CollectionJson { get; set; } = ClubCollectionSerializer.Serialize(ClubCollectionDocument.Empty);
    public long CollectionRevision { get; set; } = 1;
    public long? SourceClubId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public OyinQCommunity BotChat { get; set; } = null!;
    public ICollection<Camp> SourceCamps { get; set; } = [];

    public ClubCollectionDocument ReadCollection() => ClubCollectionSerializer.Deserialize(CollectionJson);

    public bool ReplaceCollection(ClubCollectionDocument document, DateTimeOffset now)
    {
        EnsureOwnCollection();
        ClubCollectionSerializer.Validate(document);
        if (ClubCollectionSerializer.ContentEquals(ReadCollection(), document)) return false;
        CollectionJson = ClubCollectionSerializer.Serialize(document);
        CollectionRevision++;
        UpdatedAt = now.ToUniversalTime();
        return true;
    }

    public void EnsureOwnCollection()
    {
        if (SourceClubId is not null)
            throw new InvalidOperationException("Общая коллекция обновляется автоматически. Изменяйте её в клубе-источнике.");
    }
}
