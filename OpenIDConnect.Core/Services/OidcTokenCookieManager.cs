using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace OpenIDConnect.Services;

internal sealed class OidcTokenCookieManager(IOptions<OIDCOptions> options)
{
    private const string LegacyIdTokenCookieName = "id_token";
    private const string LegacyRefreshTokenCookieName = "refresh_token";
    private const string HostPrefix = "__Host-";

    public string IdTokenCookieName => CookiePrefix + "-id-token";

    public string RefreshTokenCookieName => CookiePrefix + "-refresh-token";

    internal string CookiePrefix => NormalizePrefix(options.Value.CookiePrefix, options.Value.ClientId);

    public string? ReadIdToken(HttpContext httpContext)
    {
        return ReadCookie(httpContext, IdTokenCookieName, LegacyIdTokenCookieName);
    }

    public string? ReadRefreshToken(HttpContext httpContext)
    {
        return ReadCookie(httpContext, RefreshTokenCookieName, LegacyRefreshTokenCookieName);
    }

    public void AppendIdToken(HttpContext httpContext, string token, DateTimeOffset expires)
    {
        httpContext.Response.Cookies.Append(IdTokenCookieName, token, CreateTokenCookieOptions(expires));
        DeleteLegacyTokenCookie(httpContext, LegacyIdTokenCookieName);
    }

    public void AppendRefreshToken(HttpContext httpContext, string token, DateTimeOffset expires)
    {
        httpContext.Response.Cookies.Append(RefreshTokenCookieName, token, CreateTokenCookieOptions(expires));
        DeleteLegacyTokenCookie(httpContext, LegacyRefreshTokenCookieName);
    }

    public void ClearTokenCookies(HttpContext httpContext)
    {
        var deleteOptions = CreateDeleteCookieOptions();
        httpContext.Response.Cookies.Delete(IdTokenCookieName, deleteOptions);
        httpContext.Response.Cookies.Delete(RefreshTokenCookieName, deleteOptions);
        DeleteLegacyTokenCookie(httpContext, LegacyIdTokenCookieName);
        DeleteLegacyTokenCookie(httpContext, LegacyRefreshTokenCookieName);
    }

    public void AppendPkceState(HttpContext httpContext, string state, string protectedValue, DateTimeOffset expires)
    {
        httpContext.Response.Cookies.Append(GetPkceStateCookieName(state), protectedValue, CreateTokenCookieOptions(expires));
    }

    public string? ReadPkceState(HttpContext httpContext, string state)
    {
        return httpContext.Request.Cookies.TryGetValue(GetPkceStateCookieName(state), out var value) &&
               !string.IsNullOrWhiteSpace(value)
            ? value
            : null;
    }

    public void DeletePkceState(HttpContext httpContext, string state)
    {
        httpContext.Response.Cookies.Delete(GetPkceStateCookieName(state), CreateDeleteCookieOptions());
    }

    private string GetPkceStateCookieName(string state)
    {
        return CookiePrefix + "-pkce-" + state;
    }

    private string? ReadCookie(HttpContext httpContext, string cookieName, string legacyCookieName)
    {
        if (httpContext.Request.Cookies.TryGetValue(cookieName, out var value) &&
            !string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        if (options.Value.AcceptLegacyCookieNames &&
            httpContext.Request.Cookies.TryGetValue(legacyCookieName, out value) &&
            !string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return null;
    }

    private static void DeleteLegacyTokenCookie(HttpContext httpContext, string cookieName)
    {
        httpContext.Response.Cookies.Delete(cookieName, CreateDeleteCookieOptions());
    }

    private static CookieOptions CreateTokenCookieOptions(DateTimeOffset expires)
    {
        return new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            Expires = expires
        };
    }

    private static CookieOptions CreateDeleteCookieOptions()
    {
        return new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Path = "/"
        };
    }

    private static string NormalizePrefix(string? configuredPrefix, string clientId)
    {
        var rawPrefix = string.IsNullOrWhiteSpace(configuredPrefix)
            ? "mbf-" + clientId
            : configuredPrefix.Trim();

        if (rawPrefix.StartsWith(HostPrefix, StringComparison.Ordinal))
        {
            rawPrefix = rawPrefix[HostPrefix.Length..];
        }

        return HostPrefix + NormalizeCookieNameSegment(rawPrefix);
    }

    private static string NormalizeCookieNameSegment(string value)
    {
        var builder = new StringBuilder(value.Length);
        var previousWasSeparator = false;

        foreach (var c in value)
        {
            var normalized = c switch
            {
                >= 'A' and <= 'Z' => (char)(c - 'A' + 'a'),
                >= 'a' and <= 'z' => c,
                >= '0' and <= '9' => c,
                '-' => c,
                _ => '-'
            };

            if (normalized == '-')
            {
                if (previousWasSeparator)
                {
                    continue;
                }

                previousWasSeparator = true;
            }
            else
            {
                previousWasSeparator = false;
            }

            builder.Append(normalized);
        }

        var normalizedSegment = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(normalizedSegment)
            ? "client"
            : normalizedSegment;
    }
}
