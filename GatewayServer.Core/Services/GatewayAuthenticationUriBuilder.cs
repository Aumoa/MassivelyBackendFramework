using GatewayServer.Options;

namespace GatewayServer.Services;

internal static class GatewayAuthenticationUriBuilder
{
    private const string DefaultOidcCallbackPath = "/auth/gateway/oidc/callback";

    public static string BuildOidcRedirectUri(GatewayAuthenticationOptions options)
    {
        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        var publicBaseUri = options.PublicBaseUri;
        if (string.IsNullOrWhiteSpace(publicBaseUri) ||
            !Uri.TryCreate(publicBaseUri, UriKind.Absolute, out var baseUri) ||
            baseUri.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException("GatewayAuthentication:PublicBaseUri must be configured as an absolute HTTP or HTTPS URI for OIDC login.");
        }

        var callbackPath = NormalizeCallbackPath(options.OidcCallbackPath);
        return new Uri(baseUri, callbackPath.TrimStart('/')).ToString();
    }

    public static string NormalizeCallbackPath(string? callbackPath)
    {
        if (string.IsNullOrWhiteSpace(callbackPath))
        {
            return DefaultOidcCallbackPath;
        }

        var normalized = callbackPath.Trim();
        return normalized.StartsWith("/", StringComparison.Ordinal)
            ? normalized
            : "/" + normalized;
    }
}
