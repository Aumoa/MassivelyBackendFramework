using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenIDConnect;

namespace OpenIDConnect.Services;

internal class JwtAuthenticationStateProvider(
    IHttpContextAccessor accessor,
    ILogger<JwtAuthenticationStateProvider> logger,
    IOptions<OIDCOptions> options,
    TokenRefreshService tokenRefreshService,
    HttpClient http,
    OidcTokenValidator tokenValidator,
    OidcTokenCookieManager cookieManager,
    IDataProtectionProvider dataProtectionProvider) : AuthenticationStateProvider, IAuthenticationStateProvider
{
    private const int PkceStateLifetimeMinutes = 10;
    private static readonly string[] RoleClaimTypes = ["groups", "roles"];
    private static readonly JwtSecurityTokenHandler TokenReader = new()
    {
        MapInboundClaims = false
    };

    private ClaimsPrincipal? m_CurrentUser;
    private string? m_LastSuccessfullyCode;
    private readonly SemaphoreSlim m_Semaphore = new(1);
    private readonly IDataProtector m_PkceProtector = dataProtectionProvider.CreateProtector("OpenIDConnect.Core.PKCE.v1");

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

        var jwtToken = cookieManager.ReadIdToken(httpContext);
        if (string.IsNullOrEmpty(jwtToken))
        {
            var authHeader = httpContext.Request.Headers.Authorization.ToString();
            if (authHeader?.StartsWith("Bearer") == true)
            {
                jwtToken = authHeader["Bearer".Length..].Trim();
            }
        }

        // If id_token is missing but refresh_token exists, attempt refresh
        if (string.IsNullOrWhiteSpace(jwtToken))
        {
            var refreshToken = cookieManager.ReadRefreshToken(httpContext);
            if (!string.IsNullOrEmpty(refreshToken))
            {
                var tokenResponse = await tokenRefreshService.TryRefreshTokenAsync();
                if (tokenResponse?.IdToken != null)
                {
                    jwtToken = tokenResponse.IdToken;
                    await PersistRolesFromUserInfoAsync(httpContext, tokenResponse);
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
            var validatedToken = await ValidateOrRefreshIdTokenAsync(jwtToken);
            if (validatedToken == null)
            {
                m_CurrentUser = new ClaimsPrincipal(new ClaimsIdentity());
                return new AuthenticationState(m_CurrentUser);
            }

            var identity = new ClaimsIdentity(validatedToken.Principal.Claims, "JwtAuthType");

            foreach (var claimType in RoleClaimTypes)
            {
                foreach (var claim in identity.FindAll(claimType).ToArray())
                {
                    foreach (var role in ParseRoleClaimValues(claim.Value))
                    {
                        identity.AddClaim(new Claim(ClaimTypes.Role, role));
                    }
                }
            }

            // The provider deliberately omits "roles" from the id_token (access-token/userinfo only),
            // so roles fetched from userinfo at login/refresh time are cached in their own cookie.
            var rolesJson = cookieManager.ReadRoles(httpContext);
            if (!string.IsNullOrEmpty(rolesJson))
            {
                foreach (var role in ParseRoleClaimValues(rolesJson))
                {
                    identity.AddClaim(new Claim(ClaimTypes.Role, role));
                }
            }

            m_CurrentUser = new ClaimsPrincipal(identity);

            var tokenExpiryUtc = validatedToken.Token.ValidTo.ToUniversalTime();
            var timeUntilExpiry = tokenExpiryUtc - DateTime.UtcNow;
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug("JWT token validated successfully. Expires at: {ValidTo} UTC (in {TimeRemaining})", tokenExpiryUtc, timeUntilExpiry);
            }
        }
        catch (SecurityTokenException ex)
        {
            logger.LogWarning(ex, "Failed to validate JWT token");
            m_CurrentUser = new ClaimsPrincipal(new ClaimsIdentity());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load JWT token validation state");
            m_CurrentUser = new ClaimsPrincipal(new ClaimsIdentity());
        }

        return new AuthenticationState(m_CurrentUser);
    }

    public string GenerateLoginUri(string redirectUri, string scope)
    {
        return CreateLoginUri(redirectUri, scope);
    }

    public async ValueTask AcceptAsync(string code, string redirectUri, string? state, CancellationToken cancellationToken = default)
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

            if (!TryConsumePkceState(state, out var codeVerifier, out var nonce))
            {
                logger.LogInformation("Failed to validate PKCE state for authorization code exchange.");
                m_LastSuccessfullyCode = null;
                return;
            }

            var formData = new Dictionary<string, string>()
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = redirectUri,
                ["client_id"] = options.Value.ClientId,
                ["client_secret"] = options.Value.ClientSecret,
                ["code_verifier"] = codeVerifier
            };

            using var content = new FormUrlEncodedContent(formData);
            using var response = await http.PostAsync(options.Value.Uri + "/token", content, cancellationToken);
            if (response.IsSuccessStatusCode == false)
            {
                if (logger.IsEnabled(LogLevel.Information))
                {
                    var oauthError = response.StatusCode == HttpStatusCode.BadRequest
                        ? await OAuthErrorResponse.TryReadErrorAsync(response, cancellationToken)
                        : null;
                    logger.LogInformation(
                        "Failed to exchange authorization code for tokens. StatusCode={StatusCode}, OAuthError={OAuthError}.",
                        response.StatusCode,
                        oauthError ?? "unknown");
                }

                m_LastSuccessfullyCode = null;
                return;
            }

            var tokenResponse = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken)
                ?? throw new InvalidOperationException("Failed to parse token response");
            if (tokenResponse.IdToken != null)
            {
                try
                {
                    var validatedToken = await tokenValidator.ValidateAsync(tokenResponse.IdToken, cancellationToken);
                    if (!HasExpectedNonce(validatedToken.Token, nonce))
                    {
                        logger.LogWarning("Received id_token nonce validation failed.");
                        m_LastSuccessfullyCode = null;
                        return;
                    }
                }
                catch (SecurityTokenException ex)
                {
                    logger.LogWarning(ex, "Received id_token validation failed.");
                    m_LastSuccessfullyCode = null;
                    return;
                }

                cookieManager.AppendIdToken(
                    httpContext,
                    tokenResponse.IdToken,
                    DateTimeOffset.UtcNow.AddSeconds(tokenResponse.ExpiresIn));

                await PersistRolesFromUserInfoAsync(httpContext, tokenResponse, cancellationToken);
            }

            if (tokenResponse.RefreshToken != null && tokenResponse.RefreshExpiresIn.HasValue)
            {
                cookieManager.AppendRefreshToken(
                    httpContext,
                    tokenResponse.RefreshToken,
                    DateTimeOffset.UtcNow.AddSeconds(tokenResponse.RefreshExpiresIn.Value));
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
        m_CurrentUser = new ClaimsPrincipal(new ClaimsIdentity());
        NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(m_CurrentUser)));
    }

    public void ClearTokenCookies(HttpContext httpContext)
    {
        cookieManager.ClearTokenCookies(httpContext);
        Clear();
    }

    public void NavigateToLogin(NavigationManager navigation, string redirectRelativeUri, string scope)
    {
        navigation.NavigateTo(CreateLocalLoginUri(navigation, redirectRelativeUri, scope), forceLoad: true);
    }

    private static string CreateLocalLoginUri(NavigationManager navigation, string redirectRelativeUri, string scope)
    {
        var baseUri = new Uri(navigation.BaseUri, UriKind.Absolute);
        var redirectUri = new Uri(baseUri, redirectRelativeUri).ToString();
        var localLoginUri = new Uri(baseUri, "_oidc/login").ToString();

        return QueryHelpers.AddQueryString(localLoginUri, new Dictionary<string, string?>
        {
            ["redirect_uri"] = redirectUri,
            ["scope"] = scope
        });
    }

    private string CreateLoginUri(string redirectUri, string scope)
    {
        var httpContext = accessor.HttpContext ?? throw new InvalidOperationException("HttpContext is not available");
        var codeVerifier = CreateCodeVerifier();
        var state = CreateCodeVerifier();
        var nonce = CreateCodeVerifier();
        var codeChallenge = CreateCodeChallenge(codeVerifier);
        cookieManager.AppendPkceState(
            httpContext,
            state,
            ProtectPkceState(state, codeVerifier, nonce),
            DateTimeOffset.UtcNow.AddMinutes(PkceStateLifetimeMinutes));

        scope = ScopePolicy.ExpandAllForExternalClient(scope);
        if (ScopePolicy.TryNormalize(scope, false, out var normalizedScope, out _))
        {
            scope = normalizedScope;
        }

        return QueryHelpers.AddQueryString(options.Value.Uri.TrimEnd('/') + "/authorize", new Dictionary<string, string?>
        {
            ["client_id"] = options.Value.ClientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["scope"] = scope,
            ["state"] = state,
            ["nonce"] = nonce,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256"
        });
    }

    private async Task<ValidatedIdToken?> ValidateOrRefreshIdTokenAsync(string jwtToken)
    {
        if (ShouldRefresh(jwtToken))
        {
            logger.LogTrace("JWT token is expired or near expiration, attempting refresh.");

            var tokenResponse = await tokenRefreshService.TryRefreshTokenAsync();
            if (tokenResponse?.IdToken == null)
            {
                logger.LogInformation("Token refresh failed, user needs to re-login");
                return null;
            }

            jwtToken = tokenResponse.IdToken;
            await PersistRolesFromUserInfoAsync(accessor.HttpContext, tokenResponse);
        }

        try
        {
            return await tokenValidator.ValidateAsync(jwtToken);
        }
        catch (SecurityTokenExpiredException)
        {
            logger.LogTrace("JWT token expired during validation, attempting refresh.");

            var tokenResponse = await tokenRefreshService.TryRefreshTokenAsync();
            if (tokenResponse?.IdToken == null)
            {
                logger.LogInformation("Token refresh failed, user needs to re-login");
                return null;
            }

            await PersistRolesFromUserInfoAsync(accessor.HttpContext, tokenResponse);
            return await tokenValidator.ValidateAsync(tokenResponse.IdToken);
        }
    }

    private bool ShouldRefresh(string jwtToken)
    {
        try
        {
            var token = TokenReader.ReadJwtToken(jwtToken);
            var tokenExpiryUtc = token.ValidTo.ToUniversalTime();
            var now = DateTime.UtcNow;
            var bufferTime = now.AddSeconds(1);

            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug("Token expiry check - ValidTo: {ValidTo} (UTC: {ValidToUtc}), Now: {Now}, Buffer: {Buffer}", token.ValidTo, tokenExpiryUtc, now, bufferTime);
            }

            return tokenExpiryUtc < bufferTime;
        }
        catch
        {
            return false;
        }
    }

    private string ProtectPkceState(string state, string codeVerifier, string nonce)
    {
        return m_PkceProtector.Protect($"{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}.{state}.{codeVerifier}.{nonce}");
    }

    private bool TryConsumePkceState(string? state, out string codeVerifier, out string nonce)
    {
        codeVerifier = string.Empty;
        nonce = string.Empty;
        if (string.IsNullOrWhiteSpace(state) || !IsValidPkceParameter(state))
        {
            return false;
        }

        var httpContext = accessor.HttpContext;
        if (httpContext == null)
        {
            return false;
        }

        var protectedValue = cookieManager.ReadPkceState(httpContext, state);
        cookieManager.DeletePkceState(httpContext, state);
        return TryUnprotectPkceState(protectedValue, state, out codeVerifier, out nonce);
    }

    private bool TryUnprotectPkceState(string? protectedValue, string expectedState, out string codeVerifier, out string nonce)
    {
        codeVerifier = string.Empty;
        nonce = string.Empty;
        if (string.IsNullOrWhiteSpace(protectedValue))
        {
            return false;
        }

        try
        {
            var unprotected = m_PkceProtector.Unprotect(protectedValue);
            var values = unprotected.Split('.', 4);
            if (values.Length != 4)
            {
                return false;
            }

            if (!long.TryParse(values[0], out var issuedAtSeconds))
            {
                return false;
            }

            var issuedAt = DateTimeOffset.FromUnixTimeSeconds(issuedAtSeconds);
            var now = DateTimeOffset.UtcNow;
            if (issuedAt > now.AddMinutes(1) || now - issuedAt > TimeSpan.FromMinutes(PkceStateLifetimeMinutes))
            {
                return false;
            }

            if (!string.Equals(values[1], expectedState, StringComparison.Ordinal) ||
                !IsValidPkceParameter(values[2]) ||
                !IsValidPkceParameter(values[3]))
            {
                return false;
            }

            codeVerifier = values[2];
            nonce = values[3];
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool HasExpectedNonce(JwtSecurityToken token, string expectedNonce)
    {
        return token.Claims.Any(claim =>
            claim.Type == JwtRegisteredClaimNames.Nonce &&
            string.Equals(claim.Value, expectedNonce, StringComparison.Ordinal));
    }

    private static string CreateCodeVerifier()
    {
        return Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
    }

    private static string CreateCodeChallenge(string verifier)
    {
        return Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    private async Task PersistRolesFromUserInfoAsync(HttpContext? httpContext, TokenResponse tokenResponse, CancellationToken cancellationToken = default)
    {
        if (httpContext == null || string.IsNullOrEmpty(tokenResponse.AccessToken))
        {
            return;
        }

        try
        {
            using var response = await http.PostAsJsonAsync(
                options.Value.Uri.TrimEnd('/') + "/userinfo",
                new { token = tokenResponse.AccessToken },
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return;
            }

            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            if (document.RootElement.TryGetProperty("roles", out var rolesElement) && rolesElement.ValueKind == JsonValueKind.Array)
            {
                var roles = rolesElement.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.String)
                    .Select(e => e.GetString()!)
                    .ToArray();
                cookieManager.AppendRoles(httpContext, JsonSerializer.Serialize(roles), DateTimeOffset.UtcNow.AddSeconds(tokenResponse.ExpiresIn));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to fetch roles from the userinfo endpoint.");
        }
    }

    private static IEnumerable<string> ParseRoleClaimValues(string value)
    {
        try
        {
            return JsonSerializer.Deserialize<string[]>(value) ?? [];
        }
        catch (JsonException)
        {
            return [value];
        }
    }

    private static bool IsValidPkceParameter(string value)
    {
        return value.Length is >= 43 and <= 128 && value.All(static c =>
            c is >= 'A' and <= 'Z' ||
            c is >= 'a' and <= 'z' ||
            c is >= '0' and <= '9' ||
            c is '-' or '.' or '_' or '~');
    }
}
