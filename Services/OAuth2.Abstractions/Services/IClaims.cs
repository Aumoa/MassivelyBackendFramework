using OAuth2.DTO;

namespace OAuth2.Services;

public interface IClaims
{
    ValueTask<Claim[]> GetClaimsAsync(string accountId, CancellationToken cancellationToken = default);
    ValueTask AddClaimAsync(string accountId, ClaimName name, string value, CancellationToken cancellationToken = default);
    ValueTask SetUniqueClaimAsync(string accountId, ClaimName name, string value, CancellationToken cancellationToken = default);
    ValueTask RemoveClaimAsync(long id, CancellationToken cancellationToken = default);
    ValueTask RemoveClaimsAsync(string accountId, ClaimName name, CancellationToken cancellationToken = default);
}
