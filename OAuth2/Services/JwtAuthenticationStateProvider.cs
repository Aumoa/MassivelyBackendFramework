using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace OAuth2.Services;

public class JwtAuthenticationStateProvider(IHttpContextAccessor accessor) : AuthenticationStateProvider
{
    private ClaimsPrincipal? m_CurrentUser;
    private string? m_AccessToken;
    private string? m_Name;
    private string? m_Email;
    private string? m_Picture;
    private string? m_Sub;

    public override Task<AuthenticationState> GetAuthenticationStateAsync()
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
                    m_AccessToken = httpContext.Request.Cookies["access_token"];
                    m_Sub = m_CurrentUser.FindFirstValue(JwtRegisteredClaimNames.Sub);
                    m_Name = m_CurrentUser.FindFirstValue(JwtRegisteredClaimNames.Name);
                    m_Email = m_CurrentUser.FindFirstValue(JwtRegisteredClaimNames.Email);
                    m_Picture = m_CurrentUser.FindFirstValue(JwtRegisteredClaimNames.Picture);
                }
            }

            m_CurrentUser ??= new ClaimsPrincipal(new ClaimsIdentity());
        }

        return Task.FromResult(new AuthenticationState(m_CurrentUser));
    }

    public string? AccessToken => m_AccessToken;
    public string? Sub => m_Sub;
    public string? Name => m_Name;
    public string? Email => m_Email;
    public string? Picture => m_Picture;
}
