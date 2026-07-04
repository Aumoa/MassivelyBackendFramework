using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OAuth2.DTO;

namespace OpenIDConnect.Services;

internal class TokenRefreshService(
    IHttpContextAccessor httpContextAccessor,
    IOptions<OIDCOptions> oidcOptions,
    ILogger<TokenRefreshService> logger,
    HttpClient httpClient,
    OidcTokenValidator tokenValidator,
    OidcTokenCookieManager cookieManager)
{
    public async Task<TokenResponse?> TryRefreshTokenAsync(CancellationToken cancellationToken = default)
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext == null)
        {
            logger.LogWarning("HttpContext is null, cannot refresh token");
            return null;
        }

        if (httpContext.Response.HasStarted)
        {
            logger.LogDebug("Response headers were already sent; token refresh was skipped to avoid losing rotated refresh-token cookies.");
            return null;
        }

        var refreshToken = cookieManager.ReadRefreshToken(httpContext);
        if (string.IsNullOrEmpty(refreshToken))
        {
            logger.LogInformation("No refresh token found");
            return null;
        }

        try
        {
            var formData = new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["client_id"] = oidcOptions.Value.ClientId,
                ["client_secret"] = oidcOptions.Value.ClientSecret
            };

            using var content = new FormUrlEncodedContent(formData);
            using var response = await httpClient.PostAsync($"{oidcOptions.Value.Uri}/api/v1/token", content, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                var oauthError = OAuthErrorResponse.TryGetError(errorContent);
                if (IsExpectedRefreshTokenRejection(oauthError))
                {
                    logger.LogInformation(
                        "Refresh token was rejected by the token endpoint; clearing local login cookies. StatusCode={StatusCode}, OAuthError={OAuthError}.",
                        response.StatusCode,
                        oauthError ?? "unknown");
                }
                else
                {
                    logger.LogWarning(
                        "Token refresh failed. StatusCode={StatusCode}, OAuthError={OAuthError}.",
                        response.StatusCode,
                        oauthError ?? "unknown");
                }

                // Refresh token invalid, delete cookies
                cookieManager.ClearTokenCookies(httpContext);
                return null;
            }

            var tokenResponse = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken);
            if (tokenResponse == null)
            {
                logger.LogError("Token response is null");
                return null;
            }

            if (!string.IsNullOrEmpty(tokenResponse.IdToken))
            {
                try
                {
                    await tokenValidator.ValidateAsync(tokenResponse.IdToken, cancellationToken);
                }
                catch (SecurityTokenException ex)
                {
                    logger.LogWarning(ex, "Refreshed id_token validation failed");
                    cookieManager.ClearTokenCookies(httpContext);
                    return null;
                }
            }

            // Update cookies with new tokens for subsequent requests
            if (!string.IsNullOrEmpty(tokenResponse.IdToken))
            {
                cookieManager.AppendIdToken(
                    httpContext,
                    tokenResponse.IdToken,
                    DateTimeOffset.UtcNow.AddSeconds(tokenResponse.ExpiresIn));
            }

            if (!string.IsNullOrEmpty(tokenResponse.RefreshToken))
            {
                cookieManager.AppendRefreshToken(
                    httpContext,
                    tokenResponse.RefreshToken,
                    DateTimeOffset.UtcNow.AddSeconds(tokenResponse.RefreshExpiresIn));
            }

            logger.LogTrace("Token refreshed successfully");
            return tokenResponse;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error refreshing token");
            return null;
        }
    }

    private static bool IsExpectedRefreshTokenRejection(string? oauthError)
    {
        return string.Equals(oauthError, "invalid_grant", StringComparison.Ordinal);
    }
}
