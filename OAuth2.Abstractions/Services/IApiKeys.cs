using OAuth2.DTO;

namespace OAuth2.Services;

public interface IApiKeys
{
    ValueTask<string> CreateApiKeyAsync(string accountId, string? allowedClientId, string? allowedScope, string name, CancellationToken cancellationToken = default);
    ValueTask<ApiKeyInfo[]> GetApiKeysAsync(string accountId, CancellationToken cancellationToken = default);
    ValueTask<ApiKeyInfo?> GetApiKeyAsync(long id, CancellationToken cancellationToken = default);
    ValueTask RemoveApiKeyAsync(long id, CancellationToken cancellationToken = default);
    ValueTask<ApiKeyInfo?> VerifyApiKeyAsync(string apiKey, CancellationToken cancellationToken = default);
}
