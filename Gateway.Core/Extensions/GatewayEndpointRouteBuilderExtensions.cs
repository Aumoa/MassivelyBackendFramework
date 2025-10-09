using Gateway.Controllers;
using Gateway.Hubs;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Gateway.Extensions;

public static class GatewayEndpointRouteBuilderExtensions
{
    public static void MapGatewayEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/gateway/status", () => GatewayController.Status());
        endpoints.MapHub<GatewayHub>("/hub/gateway");
    }
}
