using OAuth2.DTO;

namespace OAuth2.Services;

public interface IClientClaims
{
    ValueTask<ClientClaim[]> GetClaimsAsync(string clientId, CancellationToken cancellationToken = default);
    ValueTask AddClaimAsync(string clientId, string name, string value, CancellationToken cancellationToken = default);
}
