using System.Security.Claims;
using OAuth2.DTO;

namespace OAuth2.Services;

public interface IJwt
{
    string Issuer { get; }
    string Modulus { get; }
    string Exponent { get; }
    string KId { get; }
    TimeSpan ExpiresIn { get; }
    TimeSpan RefreshTokenExpiresIn { get; }

    Claim[] ConfigureClaims(in RawAccount account, string scopes, AccountClaim[] accountClaims, string? nonce, bool idToken);
    string Issue(string audience, params Claim[] claims);
}
