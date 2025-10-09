using System.IdentityModel.Tokens.Jwt;

namespace OAuth2.DTO;

public readonly struct ClaimName
{
    public readonly string Name;

    private ClaimName(string name)
    {
        Name = name;
    }

    public static readonly ClaimName Email = new(JwtRegisteredClaimNames.Email);
    public static readonly ClaimName Role = new("role");

    public override string ToString() => Name;
}
