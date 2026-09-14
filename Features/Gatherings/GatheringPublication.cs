using oyinQ.Bot.Data.Entities;

namespace oyinQ.Bot.Features.Gatherings;

public static class GatheringPublication
{
    public static void Request(GameGathering gathering)
    {
        gathering.PublicationRevision++;
        // A mutation must not erase a send in progress or permit reposting an unknown new message.
        if (gathering.PublicationStatus is not (GatheringPublicationStatus.Preparing or GatheringPublicationStatus.Delivering or GatheringPublicationStatus.DeliveryUnknown))
            gathering.PublicationStatus = GatheringPublicationStatus.Pending;
    }
}
