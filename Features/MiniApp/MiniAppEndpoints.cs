namespace oyinQ.Bot.Features.MiniApp;

public static class MiniAppEndpoints
{
    public static IEndpointRouteBuilder MapMiniAppEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/miniapp");
        group.AddEndpointFilter<MiniAppIdentityFilter>();
        group.MapProfileEndpoints();
        group.MapPlayEndpoints();
        group.MapPlanningEndpoints();
        group.MapNotificationEndpoints();
        group.MapProfileCollectionEndpoints();
        group.MapCommunityEndpoints();
        group.MapCatalogEndpoints();
        group.MapAdminEndpoints();
        group.MapReleaseEndpoints();
        group.MapClubEndpoints();
        group.MapCampEndpoints();
        group.MapBggEndpoints();
        group.MapGatheringEndpoints();
        return endpoints;
    }
}
