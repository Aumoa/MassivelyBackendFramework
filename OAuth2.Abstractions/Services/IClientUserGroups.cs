using OAuth2.DTO;

namespace OAuth2.Services;

public interface IClientUserGroups
{
    ValueTask<AccountClaim[]> GetClientUserGroupsAsync(string id, string accountId, CancellationToken cancellationToken = default);
}
