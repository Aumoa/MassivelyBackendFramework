using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OAuth2.DTO;
using HostOptions = OAuth2.Options.HostOptions;

namespace OAuth2.Services;

public sealed record CachedAuthorizationSession(string AccountId, long? AuthTime, JwtSecurityToken Token);

public sealed class CachedAuthorizationSessionService(
    IAccesses accesses,
    IJwt jwt,
    IOptions<HostOptions> options,
    ILogger<CachedAuthorizationSessionService> logger)
{
    public const string CookiePrefix = "cached_jwt_";
    private static readonly JwtSecurityTokenHandler TokenHandler = new();

    public bool TryGetAccountId(string cookieName, out string accountId)
    {
        accountId = string.Empty;
        if (!cookieName.StartsWith(CookiePrefix, StringComparison.Ordinal))
        {
            return false;
        }

        accountId = cookieName[CookiePrefix.Length..];
        return !string.IsNullOrWhiteSpace(accountId);
    }

    public async ValueTask<CachedAuthorizationSession?> TryGetSessionAsync(
        HttpContext httpContext,
        string cookieName,
        string cookieValue,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetAccountId(cookieName, out var accountId))
        {
            return null;
        }

        JwtSecurityToken cachedJwt;
        try
        {
            var validationParams = jwt.GetValidationParameters();
            validationParams.ValidateLifetime = false;
            TokenHandler.ValidateToken(cookieValue, validationParams, out var validatedToken);
            cachedJwt = (JwtSecurityToken)validatedToken;
        }
        catch (SecurityTokenException e)
        {
            logger.LogWarning("{Key} token validation failed: {Message}", cookieName, e.Message);
            DeleteSessionIfPossible(httpContext, accountId);
            return null;
        }
        catch (Exception e)
        {
            logger.LogWarning("Failed to process cached jwt token. {Message}", e.Message);
            DeleteSessionIfPossible(httpContext, accountId);
            return null;
        }

        var cachedAccountId = cachedJwt.Claims.FirstOrDefault(p => p.Type == "id")?.Value;
        if (string.IsNullOrWhiteSpace(cachedAccountId) ||
            !string.Equals(cachedAccountId, accountId, StringComparison.Ordinal))
        {
            DeleteSessionIfPossible(httpContext, accountId);
            return null;
        }

        var accessToken = cachedJwt.Claims.FirstOrDefault(p => p.Type == "access_token")?.Value;
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            DeleteSessionIfPossible(httpContext, accountId);
            return null;
        }

        var access = await accesses.VerifyAsync(accessToken, cancellationToken);
        if (!access.HasValue)
        {
            if (httpContext.Response.HasStarted)
            {
                logger.LogDebug("Cached jwt access token expired after response headers were sent.");
                return null;
            }

            var refreshToken = cachedJwt.Claims.FirstOrDefault(p => p.Type == "refresh_token")?.Value;
            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                DeleteSessionIfPossible(httpContext, accountId);
                return null;
            }

            access = await accesses.RefreshAccessAsync(refreshToken, jwt.ExpiresIn, jwt.RefreshTokenExpiresIn, cancellationToken);
            if (!access.HasValue)
            {
                DeleteSessionIfPossible(httpContext, accountId);
                return null;
            }

            cachedJwt = RefreshCachedJwt(httpContext, accountId, cachedJwt, access.Value);
        }

        if (!IsMatchingSession(cachedJwt, access.Value, accountId))
        {
            DeleteSessionIfPossible(httpContext, accountId);
            return null;
        }

        return new CachedAuthorizationSession(accountId, access.Value.AuthTime ?? GetAuthTime(cachedJwt), cachedJwt);
    }

    public void AppendSession(HttpContext httpContext, string accountId, string token)
    {
        httpContext.Response.Cookies.Append($"{CookiePrefix}{accountId}", token, CreateCookieOptions());
    }

    public void DeleteSession(HttpContext httpContext, string accountId)
    {
        httpContext.Response.Cookies.Delete($"{CookiePrefix}{accountId}", CreateDeleteCookieOptions("/"));
        httpContext.Response.Cookies.Delete($"{CookiePrefix}{accountId}", CreateDeleteCookieOptions("/authorize"));
    }

    private JwtSecurityToken RefreshCachedJwt(HttpContext httpContext, string accountId, JwtSecurityToken cachedJwt, Access access)
    {
        var claims = cachedJwt.Claims.Where(p => p.Type is not ("access_token" or "refresh_token"));
        var newCachedJwt = jwt.Issue(options.Value.ClientId, [
            .. claims,
            new Claim("access_token", access.AccessToken),
            new Claim("refresh_token", access.RefreshToken)
        ]);

        AppendSession(httpContext, accountId, newCachedJwt);
        return TokenHandler.ReadJwtToken(newCachedJwt);
    }

    private void DeleteSessionIfPossible(HttpContext httpContext, string accountId)
    {
        if (!httpContext.Response.HasStarted)
        {
            DeleteSession(httpContext, accountId);
        }
    }

    private static bool IsMatchingSession(JwtSecurityToken cachedJwt, Access access, string accountId)
    {
        if (!string.Equals(access.Id, accountId, StringComparison.Ordinal))
        {
            return false;
        }

        var tokenSub = cachedJwt.Claims.FirstOrDefault(p => p.Type == JwtRegisteredClaimNames.Sub)?.Value;
        return string.IsNullOrWhiteSpace(tokenSub) ||
               string.Equals(tokenSub, access.Sub, StringComparison.Ordinal);
    }

    private static long? GetAuthTime(JwtSecurityToken jwt)
    {
        var authTime = jwt.Claims.FirstOrDefault(p => p.Type == "auth_time")?.Value;
        return long.TryParse(authTime, out var parsed) ? parsed : null;
    }

    private static CookieOptions CreateCookieOptions()
    {
        return new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            Expires = DateTimeOffset.UtcNow.AddYears(10)
        };
    }

    private static CookieOptions CreateDeleteCookieOptions(string path)
    {
        return new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Path = path
        };
    }
}
