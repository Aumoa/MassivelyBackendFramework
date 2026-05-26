namespace OAuth2.Services;

public interface IOAuthGrants
{
    ValueTask<string[]> GetGrantedScopesAsync(string accountId, string clientId, CancellationToken cancellationToken = default);
    ValueTask GrantScopesAsync(string accountId, string clientId, string scopes, CancellationToken cancellationToken = default);
}
