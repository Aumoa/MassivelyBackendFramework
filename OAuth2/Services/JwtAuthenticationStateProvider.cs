using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.IdentityModel.Tokens;
using OAuth2.DTO;

namespace OAuth2.Services;

public class JwtAuthenticationStateProvider(IHttpContextAccessor accessor, IAccesses accesses, IJwt jwt, ScopedSemaphore sem) : AuthenticationStateProvider
{
    private ClaimsPrincipal? m_CurrentUser;
    private Access? m_CurrentAccess;

    public string? Id => m_CurrentAccess?.Id;

    public string? AccessToken
    {
        get
        {
            var httpContext = accessor.HttpContext;
            return httpContext?.Request.Cookies["access_token"];
        }
    }

    private string? RefreshToken
    {
        get
        {
            var httpContext = accessor.HttpContext;
            return httpContext?.Request.Cookies["refresh_token"];
        }
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        await sem.WaitAsync();

        try
        {
            if (m_CurrentUser == null)
            {
                var httpContext = accessor.HttpContext;
                if (httpContext != null)
                {
                    var jwtToken = httpContext.Request.Cookies["id_token"];
                    if (!string.IsNullOrEmpty(jwtToken))
                    {
                        try
                        {
                            var handler = new JwtSecurityTokenHandler();

                            // Validate JWT without lifetime validation.
                            // The actual session validity is determined by the access_token
                            // and refresh_token stored in Redis, not by the JWT expiration.
                            var validationParams = jwt.GetValidationParameters();
                            validationParams.ValidateLifetime = false;

                            var principal = handler.ValidateToken(jwtToken, validationParams, out var validatedToken);
                            var token = (JwtSecurityToken)validatedToken;
                            var claims = token.Claims.ToList();
                            var identity = new ClaimsIdentity(claims, "JwtAuthType");
                            m_CurrentUser = new ClaimsPrincipal(identity);
                        }
                        catch (Exception)
                        {
                            // JWT is cryptographically invalid (not just expired) - clear cookies
                            ClearAuthenticationState(httpContext);
                            return new AuthenticationState(m_CurrentUser ?? new ClaimsPrincipal());
                        }
                    }
                }

                m_CurrentUser ??= new ClaimsPrincipal(new ClaimsIdentity());
            }

            if (m_CurrentUser?.Identity?.IsAuthenticated == true)
            {
                m_CurrentAccess = null;
                var idTokenSub = GetClaimValue(JwtRegisteredClaimNames.Sub);
                var at = AccessToken;
                if (string.IsNullOrEmpty(at) == false)
                {
                    var access = await accesses.VerifyAsync(at);
                    if (access.HasValue)
                    {
                        if (IsMatchingTokenSubject(idTokenSub, access.Value) == false)
                        {
                            ClearAuthenticationState(accessor.HttpContext);
                            return new AuthenticationState(m_CurrentUser ?? new ClaimsPrincipal());
                        }

                        m_CurrentAccess = access.Value;
                        return new AuthenticationState(m_CurrentUser ?? new ClaimsPrincipal());
                    }
                }

                var rt = RefreshToken;
                if (string.IsNullOrEmpty(rt) == false)
                {
                    var access = await accesses.RefreshAccessAsync(rt, jwt.ExpiresIn, jwt.RefreshTokenExpiresIn);
                    if (access.HasValue)
                    {
                        if (IsMatchingTokenSubject(idTokenSub, access.Value) == false)
                        {
                            ClearAuthenticationState(accessor.HttpContext);
                            return new AuthenticationState(m_CurrentUser ?? new ClaimsPrincipal());
                        }

                        var httpContext = accessor.HttpContext;
                        if (httpContext != null)
                        {
                            var cookieOptions = new CookieOptions
                            {
                                HttpOnly = true,
                                Secure = true,
                                SameSite = SameSiteMode.Strict,
                                Expires = DateTimeOffset.UtcNow.Add(jwt.ExpiresIn)
                            };
                            httpContext.Response.Cookies.Append("access_token", access.Value.AccessToken, cookieOptions);
                            var refreshCookieOptions = new CookieOptions
                            {
                                HttpOnly = true,
                                Secure = true,
                                SameSite = SameSiteMode.Strict,
                                Expires = DateTimeOffset.UtcNow.Add(jwt.RefreshTokenExpiresIn)
                            };
                            httpContext.Response.Cookies.Append("refresh_token", access.Value.RefreshToken, refreshCookieOptions);
                            m_CurrentAccess = access.Value;
                            return new AuthenticationState(m_CurrentUser ?? new ClaimsPrincipal());
                        }
                    }
                }

                // Both access_token and refresh_token are invalid; clear session
                ClearAuthenticationState(accessor.HttpContext);
            }

            return new AuthenticationState(m_CurrentUser ?? new ClaimsPrincipal());
        }
        finally
        {
            sem.Release();
        }
    }

    public string? Sub => m_CurrentAccess?.Sub;
    public string? Name => GetClaimValue(JwtRegisteredClaimNames.Name);
    public string? Email => GetClaimValue(JwtRegisteredClaimNames.Email);
    public string? Picture => GetClaimValue(JwtRegisteredClaimNames.Picture);
    public string? GivenName => GetClaimValue(JwtRegisteredClaimNames.GivenName);
    public string? FamilyName => GetClaimValue(JwtRegisteredClaimNames.FamilyName);
    public string? Nickname => GetClaimValue(JwtRegisteredClaimNames.Nickname);
    public string? PreferredUsername => GetClaimValue(JwtRegisteredClaimNames.PreferredUsername);
    public string? Website => GetClaimValue(JwtRegisteredClaimNames.Website);
    public string? Gender => GetClaimValue(JwtRegisteredClaimNames.Gender);
    public string? Birthdate => GetClaimValue(JwtRegisteredClaimNames.Birthdate);
    public string? ZoneInfo => GetClaimValue(JwtRegisteredClaimNames.ZoneInfo);
    public string? Locale => GetClaimValue(JwtRegisteredClaimNames.Locale);
    public bool? EmailVerified
    {
        get
        {
            var value = GetClaimValue(JwtRegisteredClaimNames.EmailVerified);
            return bool.TryParse(value, out var result) ? result : null;
        }
    }
    public DateTimeOffset? UpdatedAt
    {
        get
        {
            var value = GetClaimValue(JwtRegisteredClaimNames.UpdatedAt);
            return long.TryParse(value, out var seconds) ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null;
        }
    }

    private string? GetClaimValue(string type) => m_CurrentUser?.Claims.FirstOrDefault(c => c.Type == type)?.Value;

    private static bool IsMatchingTokenSubject(string? idTokenSub, Access access)
    {
        return !string.IsNullOrWhiteSpace(idTokenSub) &&
               string.Equals(idTokenSub, access.Sub, StringComparison.Ordinal);
    }

    private void ClearAuthenticationState(HttpContext? httpContext)
    {
        m_CurrentUser = new ClaimsPrincipal(new ClaimsIdentity());
        m_CurrentAccess = null;

        if (httpContext == null || httpContext.Response.HasStarted)
        {
            return;
        }

        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict
        };
        httpContext.Response.Cookies.Delete("id_token", cookieOptions);
        httpContext.Response.Cookies.Delete("access_token", cookieOptions);
        httpContext.Response.Cookies.Delete("refresh_token", cookieOptions);
        httpContext.Response.Cookies.Delete("id", cookieOptions);
    }
}
