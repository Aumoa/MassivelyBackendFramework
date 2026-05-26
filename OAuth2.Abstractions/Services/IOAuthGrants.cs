namespace OAuth2.Services;

using OAuth2.DTO;

public interface IOAuthGrants
{
    ValueTask<string[]> GetGrantedScopesAsync(string accountId, string clientId, CancellationToken cancellationToken = default);
    ValueTask<OAuthGrantInfo[]> GetGrantedClientsAsync(string accountId, CancellationToken cancellationToken = default);
    ValueTask GrantScopesAsync(string accountId, string clientId, string scopes, CancellationToken cancellationToken = default);
    ValueTask RevokeClientGrantsAsync(string accountId, string clientId, CancellationToken cancellationToken = default);
}
