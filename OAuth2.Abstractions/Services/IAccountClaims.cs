using OAuth2.DTO;

namespace OAuth2.Services;

public interface IAccountClaims
{
    ValueTask<AccountClaim[]> GetClaimsAsync(string accountId, CancellationToken cancellationToken = default);
    ValueTask AddClaimAsync(string accountId, string name, string value, CancellationToken cancellationToken = default);
    ValueTask SetUniqueClaimAsync(string accountId, string name, string value, CancellationToken cancellationToken = default);
    ValueTask RemoveClaimAsync(long id, CancellationToken cancellationToken = default);
    ValueTask RemoveClaimsAsync(string accountId, string name, CancellationToken cancellationToken = default);
}
