using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OAuth2.DTO;

namespace OpenIDConnect.Services;

internal class TokenRefreshService(
    IHttpContextAccessor httpContextAccessor,
    IOptions<OIDCOptions> oidcOptions,
    ILogger<TokenRefreshService> logger,
    HttpClient httpClient)
{
    public async Task<bool> TryRefreshTokenAsync(CancellationToken cancellationToken = default)
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext == null)
        {
            logger.LogWarning("HttpContext is null, cannot refresh token");
            return false;
        }

        var refreshToken = httpContext.Request.Cookies["refresh_token"];
        if (string.IsNullOrEmpty(refreshToken))
        {
            logger.LogInformation("No refresh token found");
            return false;
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
                logger.LogError("Token refresh failed: {StatusCode} - {Content}", response.StatusCode, errorContent);

                // Refresh token invalid, delete cookies
                httpContext.Response.Cookies.Delete("id_token");
                httpContext.Response.Cookies.Delete("refresh_token");
                return false;
            }

            var tokenResponse = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken);
            if (tokenResponse?.IdToken == null)
            {
                logger.LogError("Token response is null or missing IdToken");
                return false;
            }

            // Update cookies with new tokens
            httpContext.Response.Cookies.Append("id_token", tokenResponse.IdToken, new CookieOptions
            {
                HttpOnly = true,
                Secure = httpContext.Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                Path = "/",
                Expires = DateTimeOffset.UtcNow.AddHours(1)
            });

            if (!string.IsNullOrEmpty(tokenResponse.RefreshToken))
            {
                httpContext.Response.Cookies.Append("refresh_token", tokenResponse.RefreshToken, new CookieOptions
                {
                    HttpOnly = true,
                    Secure = httpContext.Request.IsHttps,
                    SameSite = SameSiteMode.Lax,
                    Path = "/",
                    Expires = DateTimeOffset.UtcNow.AddDays(30)
                });
            }

            logger.LogTrace("Token refreshed successfully");
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error refreshing token");
            return false;
        }
    }
}
