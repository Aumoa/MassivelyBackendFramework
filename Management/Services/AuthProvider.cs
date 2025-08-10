using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace Management.Services;

public class AuthProvider(IHttpContextAccessor httpContextAccessor) : AuthenticationStateProvider
{
    private ClaimsPrincipal m_CurrentUser = new(new ClaimsIdentity());

    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var user = httpContextAccessor.HttpContext?.User ?? new ClaimsPrincipal(new ClaimsPrincipal());
        return Task.FromResult(new AuthenticationState(user));
    }
}
