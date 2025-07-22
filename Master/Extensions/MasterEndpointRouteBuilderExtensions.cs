using Master.Hubs;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Master.Extensions;

public static class MasterEndpointRouteBuilderExtensions
{
    public static void MapMasterEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/master/status", async context =>
        {
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("{\"status\":\"ok\"}");
        });

        endpoints.MapHub<MasterHub>("/hub/master");
    }
}
