using Auth.Controllers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Auth.Extensions;

public static class AuthEndpointRouteBuilderExtensions
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/auth/status", (AuthController controller) => controller.Status());
    }
}
