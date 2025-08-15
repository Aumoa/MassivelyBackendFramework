using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OAuth2.Options;

namespace OAuth2.Services;

public class JwtTokenGenerator(IOptions<JwtOptions> options)
{
    public readonly SymmetricSecurityKey Key = new(Encoding.UTF8.GetBytes(options.Value.SecretKey));

    public string Generate(string id, string[] roles, string accessToken)
    {
        var credentials = new SigningCredentials(Key, SecurityAlgorithms.HmacSha256);
        var claims = new Claim[]
        {
            new(JwtRegisteredClaimNames.Jti, id),
            new("access_token", accessToken)
        };

        var token = new JwtSecurityToken(
            issuer: options.Value.Issuer,
            audience: options.Value.Audience,
            claims: claims.Concat(roles.Select(p => new Claim("role", p))),
            expires: DateTime.UtcNow.AddMinutes(options.Value.ExpireMinutes),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
