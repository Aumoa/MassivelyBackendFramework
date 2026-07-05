using Microsoft.Extensions.Options;
using OAuth2.DTO;
using OAuth2.Options;
using OAuth2.Services;

namespace OAuth2.Controllers.Tests;

public sealed class ApiKeyCreationServiceTests
{
    [Fact]
    public async Task CreateApiKeyAsync_RejectsInternalClient()
    {
        var service = CreateService();

        var result = await service.CreateApiKeyAsync("account", "key", "oauth2", "profile");

        Assert.False(result.IsSuccess);
        Assert.Equal(ApiKeyCreationError.InternalClientForbidden, result.Error);
    }

    [Fact]
    public async Task CreateApiKeyAsync_RejectsUnknownClient()
    {
        var service = CreateService(clientExists: false);

        var result = await service.CreateApiKeyAsync("account", "key", "missing", "profile");

        Assert.False(result.IsSuccess);
        Assert.Equal(ApiKeyCreationError.UnknownClient, result.Error);
    }

    [Fact]
    public async Task CreateApiKeyAsync_RejectsScopeOutsideClientPolicy()
    {
        var service = CreateService(allowedScopes: ["profile"]);

        var result = await service.CreateApiKeyAsync("account", "key", "client", "email");

        Assert.False(result.IsSuccess);
        Assert.Equal(ApiKeyCreationError.ScopeExceedsClient, result.Error);
    }

    [Fact]
    public async Task CreateApiKeyAsync_CreatesKeyWithNormalizedPolicy()
    {
        var apiKeys = new ApiKeysStub();
        var service = CreateService(apiKeys: apiKeys, allowedScopes: ["profile", "email"]);

        var result = await service.CreateApiKeyAsync("account", " key ", " client ", "email profile");

        Assert.True(result.IsSuccess);
        Assert.Equal("api-key", result.ApiKey);
        Assert.Equal("account", apiKeys.AccountId);
        Assert.Equal("client", apiKeys.AllowedClientId);
        Assert.Equal("profile email", apiKeys.AllowedScope);
        Assert.Equal("key", apiKeys.Name);
    }

    private static ApiKeyCreationService CreateService(
        ApiKeysStub? apiKeys = null,
        bool clientExists = true,
        string[]? allowedScopes = null)
    {
        return new ApiKeyCreationService(
            apiKeys ?? new ApiKeysStub(),
            new ClientsStub(clientExists),
            new ClientClaimsStub(allowedScopes ?? ["profile"]),
            Microsoft.Extensions.Options.Options.Create(new HostOptions
            {
                ClientId = "oauth2",
                Secret = "secret",
                Uri = "https://oauth.example.test"
            }));
    }

    private sealed class ApiKeysStub : IApiKeys
    {
        public string? AccountId { get; private set; }

        public string? AllowedClientId { get; private set; }

        public string? AllowedScope { get; private set; }

        public string? Name { get; private set; }

        public ValueTask<string> CreateApiKeyAsync(string accountId, string? allowedClientId, string? allowedScope, string name, CancellationToken cancellationToken = default)
        {
            AccountId = accountId;
            AllowedClientId = allowedClientId;
            AllowedScope = allowedScope;
            Name = name;
            return ValueTask.FromResult("api-key");
        }

        public ValueTask<ApiKeyInfo[]> GetApiKeysAsync(string accountId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<ApiKeyInfo?> GetApiKeyAsync(long id, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask RemoveApiKeyAsync(long id, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<ApiKeyInfo?> VerifyApiKeyAsync(string apiKey, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class ClientsStub(bool clientExists) : IClients
    {
        public ValueTask<string> AddClientAsync(string name, string ownerId, string[] redirectUris, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<string> AddClientAsync(string clientId, string name, string ownerId, string[] redirectUris, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<ClientInfo?> GetClientAsync(string clientId, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult<ClientInfo?>(clientExists
                ? new ClientInfo(clientId, "owner", "Client", string.Empty, [], DateTime.UtcNow)
                : null);
        }

        public ValueTask<ClientInfo[]> GetClientsAsync(string ownerId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<string> NewClientSecretAsync(string clientId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<ClientSecretInfo[]> GetClientSecretsAsync(string clientId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask RemoveClientSecretAsync(long secretId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask RemoveClientAsync(string clientId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class ClientClaimsStub(string[] allowedScopes) : IClientClaims
    {
        public ValueTask<ClientClaim[]> GetClaimsAsync(string clientId, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(allowedScopes
                .Select((scope, index) => new ClientClaim(index + 1, "scope", scope))
                .ToArray());
        }

        public ValueTask AddClaimAsync(string clientId, string name, string value, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask ModifyClaimAsync(long id, string newValue, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask RemoveClaimAsync(long id, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
