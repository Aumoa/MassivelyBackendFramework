using GatewayServer.Options;
using GatewayServer.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GatewayServer.Extensions;

public static class EndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapGatewayAuthenticationCallbacks(this IEndpointRouteBuilder endpoints)
    {
        var options = endpoints.ServiceProvider.GetRequiredService<IOptions<GatewayAuthenticationOptions>>().Value;
        endpoints.MapGet(
            NormalizeCallbackPath(options.OidcCallbackPath),
            async (
                HttpContext httpContext,
                IGatewayOidcAuthenticationService oidcAuthentication,
                CancellationToken cancellationToken) =>
            {
                var code = httpContext.Request.Query["code"].ToString();
                var state = httpContext.Request.Query["state"].ToString();
                var redirectUri = UriHelper.BuildAbsolute(
                    httpContext.Request.Scheme,
                    httpContext.Request.Host,
                    httpContext.Request.PathBase,
                    httpContext.Request.Path);
                var result = await oidcAuthentication
                    .AcceptCallbackAsync(code, state, redirectUri, cancellationToken)
                    .ConfigureAwait(false);
                return result.Success
                    ? Results.Content(result.Message, "text/plain")
                    : Results.BadRequest(result.Message);
            });

        return endpoints;
    }

    private static string NormalizeCallbackPath(string callbackPath)
    {
        if (string.IsNullOrWhiteSpace(callbackPath))
        {
            return "/auth/gateway/oidc/callback";
        }

        var normalized = callbackPath.Trim();
        return normalized.StartsWith("/", StringComparison.Ordinal)
            ? normalized
            : "/" + normalized;
    }
}
