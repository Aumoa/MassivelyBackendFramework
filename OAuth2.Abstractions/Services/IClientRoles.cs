using OAuth2.DTO;

namespace OAuth2.Services;

public interface IClientRoles
{
    ValueTask<AccountClaim[]> GetAccountRolesAsync(string clientId, string accountId, CancellationToken cancellationToken = default);
    ValueTask<ClientRole[]> GetClientRolesAsync(string clientId, CancellationToken cancellationToken = default);
    ValueTask<ClientRoleAssignment[]> GetClientRoleAssignmentsAsync(string clientId, CancellationToken cancellationToken = default);
    ValueTask AddClientRoleAsync(string clientId, string roleId, string name, CancellationToken cancellationToken = default);
    ValueTask RemoveClientRoleAsync(string clientId, string roleId, CancellationToken cancellationToken = default);
    ValueTask AssignRoleAsync(string clientId, string roleId, string accountId, CancellationToken cancellationToken = default);
    ValueTask RemoveRoleAssignmentAsync(string clientId, string roleId, string accountId, CancellationToken cancellationToken = default);
}
