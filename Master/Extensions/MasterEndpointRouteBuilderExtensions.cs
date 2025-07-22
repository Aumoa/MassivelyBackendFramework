using Master.Controllers;
using Master.Hubs;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Master.Extensions;

public static class MasterEndpointRouteBuilderExtensions
{
    public static void MapMasterEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/master/status", (MasterController controller) => controller.Status());
        endpoints.MapHub<MasterHub>("/hub/master");
    }
}
