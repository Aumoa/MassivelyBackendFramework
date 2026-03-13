using OAuth2.DTO;

namespace OAuth2.Services;

public interface IAccounts
{
    ValueTask<bool> ExistsAsync(string id, CancellationToken cancellationToken = default);
    ValueTask<bool?> LoginAsync(string id, string password, CancellationToken cancellationToken = default);
    ValueTask<string> AddAsync(string id, string password, string name, string email, string verifyValue, CancellationToken cancellationToken = default);
    ValueTask<bool> RefreshVerifyCodeAsync(string sub, string verifyCode, CancellationToken cancellationToken = default);
    ValueTask<bool> VerifyAsync(string sub, string verifyCode, CancellationToken cancellationToken = default);
    ValueTask<RawAccount?> GetRawAccountAsync(string id, CancellationToken cancellationToken = default);
    ValueTask<bool> ChangePasswordAsync(string sub, string previousPassword, string newPassword, CancellationToken cancellationToken = default);
    ValueTask<string?> ResolveSubAsync(string identifier, CancellationToken cancellationToken = default);
}
