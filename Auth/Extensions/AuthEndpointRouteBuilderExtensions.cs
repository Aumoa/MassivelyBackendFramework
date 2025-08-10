using Auth.Controllers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Auth.Extensions;

public static class AuthEndpointRouteBuilderExtensions
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/auth/status", AuthController.Status);
        endpoints.MapGet("/api/auth", AuthController.ContainsAsync);
        endpoints.MapGet("/api/auth/{provider}/login", AuthController.LoginAsync);
        endpoints.MapPost("/api/auth/{id}", AuthController.RegisterAsync);
    }
}
