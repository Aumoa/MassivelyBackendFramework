using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OAuth2.Controllers;
using OAuth2.DTO;
using OAuth2.Options;
using OAuth2.Services;

namespace OAuth2.Controllers.Tests.Controllers;

public sealed class TokenControllerApiKeyTests
{
    [Fact]
    public async Task PostAsync_RejectsApiKeyWithoutClientRestriction()
    {
        var controller = CreateController(new ApiKeysStub(new ApiKeyInfo(1, "account", null, "profile", "key", DateTime.UtcNow)));

        var result = await controller.PostAsync(CreateApiKeyRequest(), CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("invalid_grant", GetResponseProperty(badRequest.Value, "error"));
    }

    [Fact]
    public async Task PostAsync_RejectsApiKeyWithoutScopeRestriction()
    {
        var controller = CreateController(new ApiKeysStub(new ApiKeyInfo(1, "account", "client", null, "key", DateTime.UtcNow)));

        var result = await controller.PostAsync(CreateApiKeyRequest(), CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("invalid_grant", GetResponseProperty(badRequest.Value, "error"));
    }

    [Fact]
    public async Task PostAsync_RejectsApiKeyAllScope()
    {
        var controller = CreateController(new ApiKeysStub(new ApiKeyInfo(1, "account", "client", "all", "key", DateTime.UtcNow)));

        var result = await controller.PostAsync(CreateApiKeyRequest(), CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("invalid_grant", GetResponseProperty(badRequest.Value, "error"));
    }

    [Fact]
    public async Task PostAsync_RejectsApiKeyScopeOutsideClientPolicy()
    {
        var controller = CreateController(
            new ApiKeysStub(new ApiKeyInfo(1, "account", "client", "email", "key", DateTime.UtcNow)),
            clientClaims: new ClientClaimsStub("profile"));

        var result = await controller.PostAsync(CreateApiKeyRequest(), CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("invalid_scope", GetResponseProperty(badRequest.Value, "error"));
    }

    [Fact]
    public async Task PostAsync_IssuesApiKeyTokenForRestrictedClientScope()
    {
        var tokenIssuer = new TokenIssuerStub();
        var controller = CreateController(
            new ApiKeysStub(new ApiKeyInfo(1, "account", "client", "profile email", "key", DateTime.UtcNow)),
            clientClaims: new ClientClaimsStub("profile", "email"),
            tokenIssuer: tokenIssuer);

        var result = await controller.PostAsync(CreateApiKeyRequest(), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<TokenResponse>(ok.Value);
        Assert.Equal("access", response.AccessToken);
        Assert.Equal("client", tokenIssuer.ClientId);
        Assert.Equal("profile email", tokenIssuer.Scope);
    }

    [Theory]
    [InlineData("profile", false)]
    [InlineData("profile offline_access", true)]
    public async Task PostAsync_ReturnsRefreshTokenOnlyForOfflineAccess(string scope, bool expectRefreshToken)
    {
        var refreshedAccess = new Access
        {
            Id = "account",
            Sub = "sub",
            AccessToken = "new-access",
            RefreshToken = "new-refresh",
            Scope = scope,
            ClientId = "client"
        };
        var controller = CreateController(
            new ApiKeysStub(null),
            clientClaims: new ClientClaimsStub("profile", "offline_access"),
            accesses: new AccessesStub(refreshedAccess));

        var result = await controller.PostAsync(new TokenRequest
        {
            GrantType = "refresh_token",
            RefreshToken = "refresh",
            ClientId = "client",
            ClientSecret = "secret"
        }, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<TokenResponse>(ok.Value);
        Assert.Equal(expectRefreshToken ? "new-refresh" : null, response.RefreshToken);
        Assert.Equal(expectRefreshToken ? 3600 : null, response.RefreshExpiresIn);
    }

    private static TokenController CreateController(
        ApiKeysStub apiKeys,
        ClientClaimsStub? clientClaims = null,
        TokenIssuerStub? tokenIssuer = null,
        AccessesStub? accesses = null)
    {
        var controller = new TokenController(
            new AuthorizationCodesStub(),
            accesses ?? new AccessesStub(),
            new JwtStub(),
            new AccountsStub(),
            new AccountClaimsStub(),
            clientClaims ?? new ClientClaimsStub("profile"),
            new ClientUserGroupsStub(),
            new ClientRolesStub(),
            tokenIssuer ?? new TokenIssuerStub(),
            Microsoft.Extensions.Options.Options.Create(new HostOptions
            {
                ClientId = "oauth2",
                Secret = "secret",
                Uri = "https://oauth.example.test"
            }),
            apiKeys,
            new ClientsStub(),
            NullLogger<TokenController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        return controller;
    }

    private static TokenRequest CreateApiKeyRequest()
    {
        return new TokenRequest
        {
            GrantType = "api_key",
            Code = "mbf_test",
            ClientId = "client"
        };
    }

    private static object? GetResponseProperty(object? value, string name)
    {
        Assert.NotNull(value);
        var property = value.GetType().GetProperty(name);
        Assert.NotNull(property);
        return property.GetValue(value);
    }

    private sealed class ApiKeysStub(ApiKeyInfo? apiKeyInfo) : IApiKeys
    {
        public ValueTask<string> CreateApiKeyAsync(string accountId, string? allowedClientId, string? allowedScope, string name, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
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
            return ValueTask.FromResult(apiKeyInfo);
        }
    }

    private sealed class ClientsStub : IClients
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
            return ValueTask.FromResult<ClientInfo?>(new ClientInfo(clientId, "owner", "Client", string.Empty, [], DateTime.UtcNow));
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

    private sealed class ClientClaimsStub(params string[] allowedScopes) : IClientClaims
    {
        public ValueTask<ClientClaim[]> GetClaimsAsync(string clientId, CancellationToken cancellationToken = default)
        {
            var claims = allowedScopes
                .Select((scope, index) => new ClientClaim(index + 1, "scope", scope))
                .Append(new ClientClaim(1000, "secret", PasswordHasher.Hash("secret")))
                .ToArray();
            return ValueTask.FromResult(claims);
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

    private sealed class TokenIssuerStub : ITokenIssuer
    {
        public string? ClientId { get; private set; }

        public string? Scope { get; private set; }

        public ValueTask<TokenIssueResult> IssueAsync(string accountId, RawAccount rawAccount, string clientId, string scope, string? nonce, CancellationToken cancellationToken = default, long? authTime = null, string? acr = null, string? userInfoClaims = null)
        {
            ClientId = clientId;
            Scope = scope;
            var access = new Access
            {
                Id = accountId,
                Sub = rawAccount.Sub,
                AccessToken = "access",
                RefreshToken = "refresh",
                Scope = scope,
                ClientId = clientId
            };

            return ValueTask.FromResult(new TokenIssueResult(new TokenResponse
            {
                AccessToken = "access",
                TokenType = "Bearer",
                ExpiresIn = 300,
                Scope = scope,
                RefreshToken = "refresh",
                RefreshExpiresIn = 3600,
                IdToken = null
            }, access));
        }
    }

    private sealed class AccountsStub : IAccounts
    {
        public ValueTask<bool> ExistsAsync(string id, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<bool?> LoginAsync(string id, string password, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<string> AddAsync(string id, string password, string name, string email, string verifyValue, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<bool> RefreshVerifyCodeAsync(string sub, string verifyCode, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<bool> VerifyAsync(string sub, string verifyCode, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<RawAccount?> GetRawAccountAsync(string id, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult<RawAccount?>(new RawAccount
            {
                Sub = "sub",
                Name = "User",
                Email = "user@example.test",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        public ValueTask<string?> GetIdFromSubAsync(string sub, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<string?> ResolveSubAsync(string identifier, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<bool> UpdateNameAsync(string id, string name, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<bool> ChangePasswordAsync(string sub, string previousPassword, string newPassword, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class AuthorizationCodesStub : IAuthorizationCodes
    {
        public ValueTask<string> PushAsync(AuthorizationCodeBody body, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<AuthorizationCodeBody?> PopAsync(string code, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<string?> GetIssuedAccessTokenAsync(string code, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask StoreIssuedAccessTokenAsync(string code, string accessToken, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class AccessesStub(Access? refreshedAccess = null) : IAccesses
    {
        public ValueTask<Access> WriteAccessAsync(string id, string sub, string scope, string clientId, TimeSpan expire, TimeSpan refreshTokenExpire, CancellationToken cancellationToken = default, long? authTime = null, string? userInfoClaims = null)
        {
            throw new NotSupportedException();
        }

        public ValueTask<Access?> VerifyAsync(string accessToken, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<Access?> VerifyRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<Access?> RefreshAccessAsync(string refreshToken, TimeSpan expire, TimeSpan refreshTokenExpire, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<Access?> RefreshAccessAsync(string refreshToken, string clientId, TimeSpan expire, TimeSpan refreshTokenExpire, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult<Access?>(refreshedAccess);
        }

        public ValueTask RevokeAsync(string accessToken, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask InvalidateAllTokensAsync(string sub, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask InvalidateClientTokensAsync(string sub, string clientId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class AccountClaimsStub : IAccountClaims
    {
        public ValueTask<AccountClaim[]> GetClaimsAsync(string accountId, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(Array.Empty<AccountClaim>());
        }

        public ValueTask AddClaimAsync(string accountId, string name, string value, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask SetUniqueClaimAsync(string accountId, string name, string value, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask RemoveClaimAsync(long id, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask RemoveClaimsAsync(string accountId, string name, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class ClientUserGroupsStub : IClientUserGroups
    {
        public ValueTask<AccountClaim[]> GetClientUserGroupsAsync(string id, string accountId, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(Array.Empty<AccountClaim>());
        }

        public ValueTask<ClientUserGroup[]> GetAllClientUserGroupsAsync(string clientId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask AddClientUserGroupAsync(string clientId, string accountId, string group, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask ModifyClientUserGroupAsync(long id, string newGroup, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask RemoveClientUserGroupAsync(long id, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class ClientRolesStub : IClientRoles
    {
        public ValueTask<AccountClaim[]> GetAccountRolesAsync(string clientId, string accountId, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(Array.Empty<AccountClaim>());
        }

        public ValueTask<ClientRole[]> GetClientRolesAsync(string clientId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<ClientRoleAssignment[]> GetClientRoleAssignmentsAsync(string clientId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask AddClientRoleAsync(string clientId, string roleId, string name, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask RemoveClientRoleAsync(string clientId, string roleId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask AssignRoleAsync(string clientId, string roleId, string accountId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask RemoveRoleAssignmentAsync(string clientId, string roleId, string accountId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class JwtStub : IJwt
    {
        public string Issuer => "issuer";

        public string Modulus => string.Empty;

        public string Exponent => string.Empty;

        public string KId => string.Empty;

        public TimeSpan ExpiresIn => TimeSpan.FromMinutes(5);

        public TimeSpan RefreshTokenExpiresIn => TimeSpan.FromHours(1);

        public Claim[] ConfigureClaims(in RawAccount account, string scopes, AccountClaim[] accountClaims, string? nonce, bool idToken, long? authTime = null, string? acr = null, string? additionalClaims = null)
        {
            throw new NotSupportedException();
        }

        public string Issue(string audience, params Claim[] claims)
        {
            throw new NotSupportedException();
        }

        public TokenValidationParameters GetValidationParameters()
        {
            throw new NotSupportedException();
        }
    }
}
