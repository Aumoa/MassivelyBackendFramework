using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OAuth2.Options;

namespace OAuth2.Services;

public class JwtTokenIssuer(IOptions<JwtOptions> options, NavigationManager nav)
{
    private static readonly TimeSpan ExpiresIn = TimeSpan.FromHours(1);

    private readonly SymmetricSecurityKey m_Key = new(Convert.FromBase64String(options.Value.Salt));

    public string Issue(params Claim[] claims)
    {
        var credentials = new SigningCredentials(m_Key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: nav.BaseUri,
            audience: nav.BaseUri,
            claims: claims,
            expires: DateTime.UtcNow.Add(ExpiresIn),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
