namespace OAuth2.Services;

public interface IAccounts
{
    ValueTask<bool> ExistsAsync(string id, CancellationToken cancellationToken = default);
    ValueTask<bool> VerifyAsync(string id, string password, CancellationToken cancellationToken = default);
    ValueTask AddAsync(string id, string password, CancellationToken cancellationToken = default);
    ValueTask RemoveAsync(string id, CancellationToken cancellationToken = default);
    ValueTask<string?> GetSubAsync(string id, CancellationToken cancellationToken = default);
}
