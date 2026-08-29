namespace oyinQ.Bot.Features.MiniApp;

public static class MiniAppEndpoints
{
    public static IEndpointRouteBuilder MapMiniAppEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/miniapp");
        group.MapCommunityEndpoints();
        group.MapAdminEndpoints();
        group.MapClubEndpoints();
        group.MapCampEndpoints();
        group.MapBggEndpoints();
        group.MapGatheringEndpoints();
        return endpoints;
    }
}
