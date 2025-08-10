using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace Management.Services;

public class AuthProvider : AuthenticationStateProvider
{
    private ClaimsPrincipal m_CurrentUser = new(new ClaimsIdentity());

    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        return Task.FromResult(new AuthenticationState(m_CurrentUser));
    }

    public void InjectPrincipal(string jwtToken)
    {
        var handler = new JwtSecurityTokenHandler();
        var token = handler.ReadJwtToken(jwtToken);
        var claims = token.Claims.ToList();
        var identity = new ClaimsIdentity(claims, "JwtAuthType");
        m_CurrentUser = new ClaimsPrincipal(identity);
        NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(m_CurrentUser)));
    }
}
