using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OAuth2.DTO;

namespace OpenIDConnect.Services;

internal class JwtAuthenticationStateProvider(
    IHttpContextAccessor accessor,
    ILogger<JwtAuthenticationStateProvider> logger,
    IOptions<OIDCOptions> options,
    TokenRefreshService tokenRefreshService,
    HttpClient http) : AuthenticationStateProvider, IAuthenticationStateProvider
{
    private ClaimsPrincipal? m_CurrentUser;
    private string? m_LastSuccessfullyCode;
    private readonly SemaphoreSlim m_Semaphore = new(1);

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        if (m_CurrentUser != null)
        {
            return new AuthenticationState(m_CurrentUser);
        }

        var httpContext = accessor.HttpContext;
        if (httpContext == null)
        {
            m_CurrentUser = new ClaimsPrincipal(new ClaimsIdentity());
            return new AuthenticationState(m_CurrentUser);
        }

        var jwtToken = httpContext.Request.Cookies["id_token"];

        // If id_token is missing but refresh_token exists, attempt refresh
        if (string.IsNullOrWhiteSpace(jwtToken))
        {
            var refreshToken = httpContext.Request.Cookies["refresh_token"];
            if (!string.IsNullOrEmpty(refreshToken))
            {
                var tokenResponse = await tokenRefreshService.TryRefreshTokenAsync();
                if (tokenResponse?.IdToken != null)
                {
                    jwtToken = tokenResponse.IdToken;
                }
            }

            if (string.IsNullOrWhiteSpace(jwtToken))
            {
                m_CurrentUser = new ClaimsPrincipal(new ClaimsIdentity());
                return new AuthenticationState(m_CurrentUser);
            }
        }

        try
        {
            var handler = new JwtSecurityTokenHandler();
            var token = handler.ReadJwtToken(jwtToken);

            // Convert ValidTo to UTC for proper comparison
            var tokenExpiryUtc = token.ValidTo.ToUniversalTime();
            var now = DateTime.UtcNow;
            var bufferTime = now.AddSeconds(30);

            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug("Token expiry check - ValidTo: {ValidTo} (UTC: {ValidToUtc}), Now: {Now}, Buffer: {Buffer}", token.ValidTo, tokenExpiryUtc, now, bufferTime);
            }

            // Check if token is expired or near expiration (30 second buffer)
            if (tokenExpiryUtc < bufferTime)
            {
                var timeRemaining = tokenExpiryUtc - now;
                if (logger.IsEnabled(LogLevel.Trace))
                {
                    logger.LogTrace("JWT token expired or near expiration (remaining: {TimeRemaining}), attempting refresh", timeRemaining);
                }

                // Try to refresh token - use the returned TokenResponse directly
                // instead of re-reading from Request.Cookies (which contains stale values)
                var tokenResponse = await tokenRefreshService.TryRefreshTokenAsync();
                if (tokenResponse?.IdToken != null)
                {
                    jwtToken = tokenResponse.IdToken;
                    token = handler.ReadJwtToken(jwtToken);
                    if (logger.IsEnabled(LogLevel.Trace))
                    {
                        logger.LogTrace("Token refreshed successfully, new expiry: {ValidTo}", token.ValidTo);
                    }
                }
                else
                {
                    // Refresh failed, user needs to re-login
                    logger.LogWarning("Token refresh failed, user needs to re-login");
                    m_CurrentUser = new ClaimsPrincipal(new ClaimsIdentity());
                    return new AuthenticationState(m_CurrentUser);
                }
            }

            var identity = new ClaimsIdentity(token.Claims, "JwtAuthType");
            foreach (var claim in identity.FindAll("groups").ToArray())
            {
                var role = claim.Value;
                identity.AddClaim(new Claim(ClaimTypes.Role, role));
            }

            m_CurrentUser = new ClaimsPrincipal(identity);

            tokenExpiryUtc = token.ValidTo.ToUniversalTime();
            var timeUntilExpiry = tokenExpiryUtc - DateTime.UtcNow;
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug("JWT token validated successfully. Expires at: {ValidTo} UTC (in {TimeRemaining})", tokenExpiryUtc, timeUntilExpiry);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to parse JWT token");
            m_CurrentUser = new ClaimsPrincipal(new ClaimsIdentity());
        }

        return new AuthenticationState(m_CurrentUser);
    }

    public string GenerateLoginUri(string redirectUri, string scope)
    {
        var clientId = Uri.EscapeDataString(options.Value.ClientId);
        redirectUri = Uri.EscapeDataString(redirectUri);
        return options.Value.Uri + $"/authorize?client_id={clientId}&redirect_uri={redirectUri}&response_type=code&scope={scope}";
    }

    public async ValueTask AcceptAsync(string code, string redirectUri, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        var httpContext = accessor.HttpContext ?? throw new InvalidOperationException("HttpContext is not available");

        await m_Semaphore.WaitAsync(cancellationToken);
        try
        {
            if (m_LastSuccessfullyCode == code)
            {
                return;
            }

            var formData = new Dictionary<string, string>()
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = redirectUri,
                ["client_id"] = options.Value.ClientId,
                ["client_secret"] = options.Value.ClientSecret
            };

            using var content = new FormUrlEncodedContent(formData);
            using var response = await http.PostAsync(options.Value.Uri + "/api/v1/token", content, cancellationToken);
            response.EnsureSuccessStatusCode();

            var tokenResponse = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken)
                ?? throw new InvalidOperationException("Failed to parse token response");
            if (tokenResponse.IdToken != null)
            {
                httpContext.Response.Cookies.Append("id_token", tokenResponse.IdToken, new CookieOptions
                {
                    HttpOnly = true,
                    Secure = true,
                    SameSite = SameSiteMode.Strict,
                    Path = "/",
                    Expires = DateTimeOffset.UtcNow.AddSeconds(tokenResponse.ExpiresIn)
                });
            }

            if (tokenResponse.RefreshToken != null)
            {
                httpContext.Response.Cookies.Append("refresh_token", tokenResponse.RefreshToken, new CookieOptions
                {
                    HttpOnly = true,
                    Secure = true,
                    SameSite = SameSiteMode.Strict,
                    Path = "/",
                    Expires = DateTimeOffset.UtcNow.AddSeconds(tokenResponse.RefreshExpiresIn)
                });
            }

            m_LastSuccessfullyCode = code;
        }
        catch
        {
            m_LastSuccessfullyCode = null;
            throw;
        }
        finally
        {
            m_Semaphore.Release();
        }
    }

    public void Clear()
    {
        m_CurrentUser = null;
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }
}
