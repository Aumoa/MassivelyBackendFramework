using Microsoft.Extensions.Options;
using OAuth2.Options;

namespace OAuth2.Services;

public sealed class ApiKeyCreationService(
    IApiKeys apiKeys,
    IClients clients,
    IClientClaims clientClaims,
    IOptions<HostOptions> hostOptions) : IApiKeyCreationService
{
    public async ValueTask<ApiKeyCreationResult> CreateApiKeyAsync(
        string accountId,
        string name,
        string? allowedClientId,
        string? allowedScope,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);

        if (string.IsNullOrWhiteSpace(name))
        {
            return ApiKeyCreationResult.Failure(ApiKeyCreationError.NameRequired);
        }

        if (string.IsNullOrWhiteSpace(allowedClientId))
        {
            return ApiKeyCreationResult.Failure(ApiKeyCreationError.AllowedClientIdRequired);
        }

        allowedClientId = allowedClientId.Trim();
        if (string.Equals(allowedClientId, hostOptions.Value.ClientId, StringComparison.Ordinal))
        {
            return ApiKeyCreationResult.Failure(ApiKeyCreationError.InternalClientForbidden);
        }

        var client = await clients.GetClientAsync(allowedClientId, cancellationToken);
        if (!client.HasValue)
        {
            return ApiKeyCreationResult.Failure(ApiKeyCreationError.UnknownClient);
        }

        if (string.IsNullOrWhiteSpace(allowedScope))
        {
            return ApiKeyCreationResult.Failure(ApiKeyCreationError.AllowedScopeRequired);
        }

        if (!ScopePolicy.TryNormalize(allowedScope, false, out var normalizedScope, out _))
        {
            return ApiKeyCreationResult.Failure(ApiKeyCreationError.InvalidScope);
        }

        var claims = await clientClaims.GetClaimsAsync(allowedClientId, cancellationToken);
        var allowedScopes = claims.Where(c => c.Name == "scope").Select(c => c.Value);
        if (!ScopePolicy.IsAllowedByClient(normalizedScope, allowedScopes))
        {
            return ApiKeyCreationResult.Failure(ApiKeyCreationError.ScopeExceedsClient);
        }

        var apiKey = await apiKeys.CreateApiKeyAsync(accountId, allowedClientId, normalizedScope, name.Trim(), cancellationToken);
        return ApiKeyCreationResult.Success(apiKey);
    }
}
