using OAuth2.DTO;

namespace OAuth2.Services;

public interface IClientUserGroups
{
    ValueTask<AccountClaim[]> GetClientUserGroupsAsync(string id, string accountId, CancellationToken cancellationToken = default);
    ValueTask<ClientUserGroup[]> GetAllClientUserGroupsAsync(string clientId, CancellationToken cancellationToken = default);
    ValueTask AddClientUserGroupAsync(string clientId, string accountId, string group, CancellationToken cancellationToken = default);
    ValueTask ModifyClientUserGroupAsync(long id, string newGroup, CancellationToken cancellationToken = default);
    ValueTask RemoveClientUserGroupAsync(long id, CancellationToken cancellationToken = default);
}
