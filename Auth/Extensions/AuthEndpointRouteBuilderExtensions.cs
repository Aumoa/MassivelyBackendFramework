using Auth.Controllers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Auth.Extensions;

public static class AuthEndpointRouteBuilderExtensions
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/auth/status", AuthController.Status);
        endpoints.MapGet("/api/auth/redirect/{provider}", AuthController.Redirect);
        endpoints.MapGet("/api/auth/login/{provider}", AuthController.Login);
    }
}
