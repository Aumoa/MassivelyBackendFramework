using System.Security.Claims;

namespace OAuth2.Services;

public interface IJwt
{
    string Issuer { get; }
    string Modulus { get; }
    string Exponent { get; }
    string KId { get; }

    string Issue(string audience, params Claim[] claims);
}
