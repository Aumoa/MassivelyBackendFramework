using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using OAuth2.DTO;

namespace OAuth2.Services;

public class JwtAuthenticationStateProvider(IHttpContextAccessor accessor, IAccesses accesses, IAccountClaims accountClaims, IAccounts accounts) : AuthenticationStateProvider
{
    private ClaimsPrincipal? m_CurrentUser;
    private string? m_Name;
    private string? m_Email;
    private string? m_Picture;
    private string? m_Sub;

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
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
            var id = (await accesses.VerifyAsync(AccessToken!))!;

            {
                var claims = await accountClaims.GetClaimsAsync(id);
                m_Name = claims.FirstOrDefault(p => p.Name == ClaimNames.Name).Value;
                m_Email = claims.FirstOrDefault(p => p.Name == ClaimNames.Email).Value;
                m_Picture = claims.FirstOrDefault(p => p.Name == ClaimNames.Picture).Value;
                m_Sub = await accounts.GetSubAsync(id);
            }
        }

        return new AuthenticationState(m_CurrentUser);
    }

    public string? AccessToken => m_CurrentUser?.Claims.FirstOrDefault(p => p.Type == "access_token")?.Value;
    public string? Name => m_Name;
    public string? Email => m_Email;
    public string? Picture => m_Picture;
    public string? Sub => m_Sub;
}
