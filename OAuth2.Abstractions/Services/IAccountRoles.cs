using OAuth2.DTO;

namespace OAuth2.Services;

public interface IAccountRoles
{
    ValueTask<AccountClaim[]> GetAccountRolesAsync(string accountId, CancellationToken cancellationToken = default);
    ValueTask<bool> HasRoleAsync(string accountId, string role, CancellationToken cancellationToken = default);
    ValueTask<AccountRole[]> GetAllAccountRolesAsync(CancellationToken cancellationToken = default);
    ValueTask AddAccountRoleAsync(string accountId, string role, CancellationToken cancellationToken = default);
    ValueTask ModifyAccountRoleAsync(long id, string newRole, CancellationToken cancellationToken = default);
    ValueTask RemoveAccountRoleAsync(long id, CancellationToken cancellationToken = default);
}
