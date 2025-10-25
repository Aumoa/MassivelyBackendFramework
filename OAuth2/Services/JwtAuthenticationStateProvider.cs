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
            var accessToken = AccessToken;
            if (string.IsNullOrEmpty(accessToken) == false)
            {
                var id = (await accesses.VerifyAsync(accessToken))!;

                {
                    var raw = (await accounts.GetRawAccountAsync(id)).Value;
                    var claims = await accountClaims.GetClaimsAsync(id);
                    m_Sub = raw.Sub;
                    m_Name = raw.Name;
                    m_Email = raw.Email;
                    m_Picture = claims.FirstOrDefault(p => p.Name == ClaimNames.Picture).Value;
                }
            }
            else
            {
                m_Sub = null;
                m_Name = null;
                m_Email = null;
                m_Picture = null;
            }
        }

        return new AuthenticationState(m_CurrentUser);
    }

    public string? AccessToken => m_CurrentUser?.Claims.FirstOrDefault(p => p.Type == "access_token")?.Value;
    public string? Sub => m_Sub;
    public string? Name => m_Name;
    public string? Email => m_Email;
    public string? Picture => m_Picture;
}
