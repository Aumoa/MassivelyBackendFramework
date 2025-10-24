using System.IdentityModel.Tokens.Jwt;

namespace OAuth2.DTO;

public static class ClaimNames
{
    public static readonly string Name = JwtRegisteredClaimNames.Name;
    public static readonly string Email = JwtRegisteredClaimNames.Email;
    public static readonly string Picture = JwtRegisteredClaimNames.Picture;
}
