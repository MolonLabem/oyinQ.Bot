using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Features.Collections;

namespace oyinQ.Bot.Data.Entities;

public sealed class Camp
{
    public long Id { get; set; }
    public string BotChatKey { get; set; } = string.Empty;
    public BotMode BotChatMode { get; private set; } = BotMode.Camp;
    public string Name { get; set; } = string.Empty;
    public long? SourceClubId { get; set; }
    public string BaseCollectionJson { get; set; } = ClubCollectionSerializer.Serialize(ClubCollectionDocument.Empty);
    public CampStatus Status { get; set; }
    public long CreatedByTelegramUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public OyinQCommunity BotChat { get; set; } = null!;
    public Club? SourceClub { get; set; }
    public ICollection<CampRegistration> Registrations { get; set; } = [];
    public ICollection<CampGameContribution> Contributions { get; set; } = [];

    public ClubCollectionDocument ReadBaseCollection() => ClubCollectionSerializer.Deserialize(BaseCollectionJson);
}
