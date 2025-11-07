using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;

namespace OAuth2.Services;

public class JwtAuthenticationStateProvider(IHttpContextAccessor accessor, IAccesses accesses, IJwt jwt, ScopedSemaphore sem) : AuthenticationStateProvider
{
    private ClaimsPrincipal? m_CurrentUser;
    private string? m_Name;
    private string? m_Email;
    private string? m_Picture;
    private string? m_Sub;

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

    private void ResetAll()
    {
        m_CurrentUser = null;
        m_Name = null;
        m_Email = null;
        m_Picture = null;
        m_Sub = null;
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
                        var handler = new JwtSecurityTokenHandler();
                        var token = handler.ReadJwtToken(jwtToken);
                        var claims = token.Claims.ToList();
                        var identity = new ClaimsIdentity(claims, "JwtAuthType");
                        var principal = new ClaimsPrincipal(identity);
                        m_CurrentUser = principal;
                        m_Sub = m_CurrentUser.FindFirstValue(JwtRegisteredClaimNames.Sub);
                        m_Name = m_CurrentUser.FindFirstValue(JwtRegisteredClaimNames.Name);
                        m_Email = m_CurrentUser.FindFirstValue(JwtRegisteredClaimNames.Email);
                        m_Picture = m_CurrentUser.FindFirstValue(JwtRegisteredClaimNames.Picture);
                    }
                }

                m_CurrentUser ??= new ClaimsPrincipal(new ClaimsIdentity());
            }

            if (m_CurrentUser != null)
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
                    var access = await accesses.RefreshAccessAsync(rt, jwt.ExpiresIn);
                    if (access.HasValue)
                    {
                        var httpContext = accessor.HttpContext;
                        if (httpContext != null)
                        {
                            httpContext.Response.Cookies.Append("access_token", access.Value.AccessToken);
                            httpContext.Response.Cookies.Append("refresh_token", access.Value.RefreshToken);
                            return new AuthenticationState(m_CurrentUser);
                        }
                    }
                }

                ResetAll();
            }

            m_CurrentUser ??= new ClaimsPrincipal();
            return new AuthenticationState(m_CurrentUser);
        }
        finally
        {
            sem.Release();
        }
    }

    public string? Sub => m_Sub;
    public string? Name => m_Name;
    public string? Email => m_Email;
    public string? Picture => m_Picture;
}
