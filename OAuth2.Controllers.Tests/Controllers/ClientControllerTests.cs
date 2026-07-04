using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using OAuth2;
using OAuth2.Controllers;
using OAuth2.DTO;
using OAuth2.Options;
using OAuth2.Services;

namespace OAuth2.Controllers.Tests.Controllers;

public sealed class ClientControllerTests
{
    [Fact]
    public async Task PostAsync_UsesExplicitClientIdWhenProvided()
    {
        var clients = new ClientsStub();
        var controller = CreateController(clients);

        var result = await controller.PostAsync(new CreateClientRequest
        {
            Name = "Example",
            ClientId = "example-app"
        }, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<CreateClientResponse>(ok.Value);
        Assert.Equal("example-app", response.Id);
        Assert.Equal("example-app", clients.ExplicitClientId);
        Assert.False(clients.GeneratedClientCreated);
    }

    [Fact]
    public async Task PostAsync_UsesGeneratedClientIdWhenClientIdIsOmitted()
    {
        var clients = new ClientsStub();
        var controller = CreateController(clients);

        var result = await controller.PostAsync(new CreateClientRequest
        {
            Name = "Example"
        }, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<CreateClientResponse>(ok.Value);
        Assert.Equal(ClientsStub.GeneratedClientId, response.Id);
        Assert.True(clients.GeneratedClientCreated);
        Assert.Null(clients.ExplicitClientId);
    }

    [Fact]
    public async Task PostAsync_RejectsInvalidExplicitClientId()
    {
        var clients = new ClientsStub();
        var controller = CreateController(clients);

        var result = await controller.PostAsync(new CreateClientRequest
        {
            Name = "Example",
            ClientId = "example app"
        }, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.False(clients.GeneratedClientCreated);
        Assert.Null(clients.ExplicitClientId);
    }

    [Fact]
    public async Task PostAsync_ReturnsConflictWhenExplicitClientIdAlreadyExists()
    {
        var clients = new ClientsStub();
        clients.DuplicateClientIds.Add("example-app");
        var controller = CreateController(clients);

        var result = await controller.PostAsync(new CreateClientRequest
        {
            Name = "Example",
            ClientId = "example-app"
        }, CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal("client_id_already_exists", GetResponseProperty(conflict.Value, "error"));
        Assert.Equal("client_id already exists", GetResponseProperty(conflict.Value, "error_description"));
        Assert.Equal("example-app", clients.ExplicitClientId);
        Assert.False(clients.GeneratedClientCreated);
    }

    private static ClientController CreateController(ClientsStub clients)
    {
        var controller = new ClientController(clients, new AccessesStub(), Microsoft.Extensions.Options.Options.Create(new HostOptions
        {
            ClientId = "oauth2",
            Secret = "secret",
            Uri = "https://oauth.example.test"
        }))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        controller.ControllerContext.HttpContext.Request.Headers.Authorization = "Bearer access-token";

        return controller;
    }

    private static object? GetResponseProperty(object? value, string name)
    {
        Assert.NotNull(value);
        var property = value.GetType().GetProperty(name);
        Assert.NotNull(property);
        return property.GetValue(value);
    }

    private sealed class ClientsStub : IClients
    {
        public const string GeneratedClientId = "generated-client-id";

        public HashSet<string> DuplicateClientIds { get; } = [];

        public bool GeneratedClientCreated { get; private set; }

        public string? ExplicitClientId { get; private set; }

        public ValueTask<string> AddClientAsync(string name, string ownerId, string[] redirectUris, CancellationToken cancellationToken = default)
        {
            GeneratedClientCreated = true;
            return ValueTask.FromResult(GeneratedClientId);
        }

        public ValueTask<string> AddClientAsync(string clientId, string name, string ownerId, string[] redirectUris, CancellationToken cancellationToken = default)
        {
            ExplicitClientId = clientId;
            if (DuplicateClientIds.Contains(clientId))
            {
                throw new ClientIdAlreadyExistsException(clientId);
            }

            return ValueTask.FromResult(clientId);
        }

        public ValueTask<ClientInfo?> GetClientAsync(string clientId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
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

    private sealed class AccessesStub : IAccesses
    {
        public ValueTask<Access> WriteAccessAsync(string id, string sub, string scope, string clientId, TimeSpan expire, TimeSpan refreshTokenExpire, CancellationToken cancellationToken = default, long? authTime = null, string? userInfoClaims = null)
        {
            throw new NotSupportedException();
        }

        public ValueTask<Access?> VerifyAsync(string accessToken, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult<Access?>(new Access
            {
                Id = "owner",
                Sub = "sub",
                AccessToken = accessToken,
                RefreshToken = "refresh-token",
                Scope = "all",
                ClientId = "oauth2"
            });
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
            throw new NotSupportedException();
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
}
