using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.IdentityModel.Tokens;

namespace OAuth2.Services;

public class JwtAuthenticationStateProvider(IHttpContextAccessor accessor, IAccesses accesses, IJwt jwt, ScopedSemaphore sem) : AuthenticationStateProvider
{
    private ClaimsPrincipal? m_CurrentUser;

    public string? Id
    {
        get
        {
            var httpContext = accessor.HttpContext;
            return httpContext?.Request.Cookies["id"];
        }
    }

    public string ? AccessToken
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
                            m_CurrentUser = new ClaimsPrincipal(new ClaimsIdentity());
                            if (httpContext != null && !httpContext.Response.HasStarted)
                            {
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
                            return new AuthenticationState(m_CurrentUser);
                        }
                    }
                }

                m_CurrentUser ??= new ClaimsPrincipal(new ClaimsIdentity());
            }

            if (m_CurrentUser?.Identity?.IsAuthenticated == true)
            {
                var at = AccessToken;
                if (string.IsNullOrEmpty(at) == false)
                {
                    var access = await accesses.VerifyAsync(at);
                    if (access.HasValue)
                    {
                        return new AuthenticationState(m_CurrentUser);
                    }
                }

                var rt = RefreshToken;
                if (string.IsNullOrEmpty(rt) == false)
                {
                    var access = await accesses.RefreshAccessAsync(rt, jwt.ExpiresIn, jwt.RefreshTokenExpiresIn);
                    if (access.HasValue)
                    {
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
                            return new AuthenticationState(m_CurrentUser);
                        }
                    }
                }

                // Both access_token and refresh_token are invalid; clear session
                m_CurrentUser = new ClaimsPrincipal(new ClaimsIdentity());
            }

            return new AuthenticationState(m_CurrentUser ?? new ClaimsPrincipal());
        }
        finally
        {
            sem.Release();
        }
    }

    public string? Sub => m_CurrentUser?.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Sub)?.Value;
    public string? Name => m_CurrentUser?.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Name)?.Value;
    public string? Email => m_CurrentUser?.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Email)?.Value;
    public string? Picture => m_CurrentUser?.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Picture)?.Value;
    public string? GivenName => m_CurrentUser?.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.GivenName)?.Value;
    public string? FamilyName => m_CurrentUser?.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.FamilyName)?.Value;
    public string? Nickname => m_CurrentUser?.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Nickname)?.Value;
    public string? PreferredUsername => m_CurrentUser?.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.PreferredUsername)?.Value;
    public string? Website => m_CurrentUser?.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Website)?.Value;
    public string? Gender => m_CurrentUser?.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Gender)?.Value;
    public string? Birthdate => m_CurrentUser?.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Birthdate)?.Value;
    public string? ZoneInfo => m_CurrentUser?.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.ZoneInfo)?.Value;
    public string? Locale => m_CurrentUser?.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Locale)?.Value;
    public bool? EmailVerified
    {
        get
        {
            var value = m_CurrentUser?.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.EmailVerified)?.Value;
            return bool.TryParse(value, out var result) ? result : null;
        }
    }
    public DateTimeOffset? UpdatedAt
    {
        get
        {
            var value = m_CurrentUser?.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.UpdatedAt)?.Value;
            return long.TryParse(value, out var seconds) ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null;
        }
    }
}
