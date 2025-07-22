using Gateway.Hubs;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Gateway.Extensions;

public static class GatewayEndpointRouteBuilderExtensions
{
    public static void MapGatewayEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/gateway/status", async context =>
        {
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("{\"status\":\"ok\"}");
        });

        endpoints.MapHub<GatewayHub>("/hub/gateway");
    }
}
