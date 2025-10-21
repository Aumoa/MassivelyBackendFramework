using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace OAuth2.Services;

public class JwtAuthenticationStateProvider(IHttpContextAccessor accessor) : AuthenticationStateProvider
{
    private ClaimsPrincipal? m_CurrentUser;

    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        if (m_CurrentUser == null)
        {
            var httpContext = accessor.HttpContext;
            if (httpContext != null)
            {
                var jwtToken = httpContext.Request.Cookies["jwt_token"];
                if (!string.IsNullOrEmpty(jwtToken))
                {
                    var handler = new JwtSecurityTokenHandler();
                    var token = handler.ReadJwtToken(jwtToken);
                    var claims = token.Claims.ToList();
                    var identity = new ClaimsIdentity(claims, "JwtAuthType");
                    var principal = new ClaimsPrincipal(identity);
                    m_CurrentUser = principal;
                }
            }

            m_CurrentUser ??= new ClaimsPrincipal(new ClaimsIdentity());
        }

        return Task.FromResult(new AuthenticationState(m_CurrentUser));
    }

    public string? AccessToken => m_CurrentUser?.Claims.FirstOrDefault(p => p.Type == "access_token")?.Value;
}
