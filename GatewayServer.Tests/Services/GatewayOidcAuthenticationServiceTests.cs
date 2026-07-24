using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using GatewayServer.Behaviors;
using GatewayServer.Options;
using GatewayServer.Protocols;
using GatewayServer.Services;
using MasterServer.ControlPlane;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace GatewayServer.Tests.Services;

public sealed class GatewayOidcAuthenticationServiceTests
{
    [Fact]
    public async Task CompletionToken_AcceptsPrincipalAfterCallback()
    {
        using var rsa = RSA.Create(2048);
        var nonce = string.Empty;
        var handler = new FakeOidcHandler(request =>
        {
            if (request.Method == HttpMethod.Get &&
                request.RequestUri?.AbsoluteUri == "https://accounts.example.test/.well-known/openid-configuration")
            {
                return Json("""
{
  "issuer": "https://accounts.example.test",
  "authorization_endpoint": "https://accounts.example.test/authorize",
  "token_endpoint": "https://accounts.example.test/token",
  "jwks_uri": "https://accounts.example.test/jwks"
}
""");
            }

            if (request.Method == HttpMethod.Get &&
                request.RequestUri?.AbsoluteUri == "https://accounts.example.test/jwks")
            {
                return Json(CreateJwks(rsa));
            }

            if (request.Method == HttpMethod.Post &&
                request.RequestUri?.AbsoluteUri == "https://accounts.example.test/token")
            {
                var form = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                Assert.Contains("grant_type=authorization_code", form);
                Assert.Contains("code=code-alpha", form);
                Assert.Contains("redirect_uri=https%3A%2F%2Fgateway.example.test%2Fauth%2Fgateway%2Foidc%2Fcallback", form);
                Assert.Contains("client_id=gateway-client", form);
                Assert.Contains("code_verifier=", form);

                return Json($$"""
{
  "id_token": "{{CreateIdToken(rsa, nonce)}}",
  "expires_in": 3600,
  "token_type": "Bearer"
}
""");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var service = CreateService(handler);
        var method = GatewayAuthenticationMethodDefinition.OidcAuthorizationCode(
            "oidc",
            "OIDC",
            "https://accounts.example.test",
            "gateway-client",
            "openid profile email");
        await using var client = await CreateClientAsync();

        var challenge = await service.CreateChallengeAsync(
            method,
            client.Client,
            "world",
            new GatewayBackendServerHandle("server-alpha"),
            CancellationToken.None);

        var loginUri = new Uri(challenge.LoginUri);
        var loginQuery = QueryHelpers.ParseQuery(loginUri.Query);
        nonce = loginQuery["nonce"].ToString();
        var state = loginQuery["state"].ToString();

        Assert.Equal(GatewayClientAuthenticationMethodKind.OidcAuthorizationCode, challenge.Kind);
        Assert.Equal("oidc", challenge.MethodId);
        Assert.StartsWith("gwo_", challenge.CompletionToken, StringComparison.Ordinal);
        Assert.Equal("https://accounts.example.test/authorize", loginUri.GetLeftPart(UriPartial.Path));
        Assert.Equal("gateway-client", loginQuery["client_id"].ToString());
        Assert.Equal("https://gateway.example.test/auth/gateway/oidc/callback", loginQuery["redirect_uri"].ToString());
        Assert.Equal("S256", loginQuery["code_challenge_method"].ToString());

        var pending = await service.ValidateOidcCompletionTokenAsync(challenge.CompletionToken, CancellationToken.None);
        Assert.False(pending.Success);
        Assert.Equal("Gateway OIDC login has not completed.", pending.ErrorMessage);

        var callback = await service.AcceptCallbackAsync(
            "code-alpha",
            state,
            "https://gateway.example.test/auth/gateway/oidc/callback",
            CancellationToken.None);
        Assert.True(callback.Success);

        var accepted = await service.ValidateOidcCompletionTokenAsync(challenge.CompletionToken, CancellationToken.None);
        Assert.True(accepted.Success);
        Assert.NotNull(accepted.Principal);
        Assert.Equal("player-oidc", accepted.Principal.SubjectId);
        Assert.Equal(GatewayAuthenticationMethodKind.OidcAuthorizationCode, accepted.Principal.AuthenticationMethodKind);
        Assert.Equal("oidc", accepted.Principal.AuthenticationMethodId);
    }

    [Fact]
    public async Task CreateChallenge_RejectsWhenClientPendingCapacityIsExhausted()
    {
        var service = CreateService(
            CreateDiscoveryOnlyHandler(),
            new GatewayAuthenticationOptions
            {
                PublicBaseUri = "https://gateway.example.test",
                MaxOidcPendingLoginsPerClient = 1,
                MaxOidcLoginCreationsPerClientPerWindow = 0
            });
        var method = CreateMethod();
        await using var client = await CreateClientAsync();

        _ = await service.CreateChallengeAsync(
            method,
            client.Client,
            "world",
            null,
            CancellationToken.None);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            _ = await service.CreateChallengeAsync(
                method,
                client.Client,
                "world",
                null,
                CancellationToken.None);
        });
        Assert.Equal("Gateway OIDC pending login capacity is exhausted for this client.", ex.Message);
    }

    [Fact]
    public async Task CancelPendingLogins_RemovesTransactionsForClient()
    {
        var service = CreateService(CreateDiscoveryOnlyHandler());
        var method = CreateMethod();
        await using var client = await CreateClientAsync();

        var challenge = await service.CreateChallengeAsync(
            method,
            client.Client,
            "world",
            null,
            CancellationToken.None);

        service.CancelPendingLogins(client.Client);

        var result = await service.ValidateOidcCompletionTokenAsync(challenge.CompletionToken, CancellationToken.None);
        Assert.False(result.Success);
        Assert.Equal("Gateway OIDC login transaction was not found.", result.ErrorMessage);
    }

    [Fact]
    public void BuildOidcRedirectUri_UsesConfiguredPublicBaseUri()
    {
        var redirectUri = GatewayAuthenticationUriBuilder.BuildOidcRedirectUri(new GatewayAuthenticationOptions
        {
            PublicBaseUri = "https://gateway.example.test/root/",
            OidcCallbackPath = "auth/callback"
        });

        Assert.Equal("https://gateway.example.test/root/auth/callback", redirectUri);
    }

    private static GatewayOidcAuthenticationService CreateService(
        HttpMessageHandler handler,
        GatewayAuthenticationOptions? options = null)
    {
        return new GatewayOidcAuthenticationService(
            Microsoft.Extensions.Options.Options.Create(options ?? new GatewayAuthenticationOptions
            {
                PublicBaseUri = "https://gateway.example.test",
                OidcLoginLifetimeSeconds = 600
            }),
            new StaticHttpClientFactory(new HttpClient(handler)),
            NullLogger<GatewayOidcAuthenticationService>.Instance);
    }

    private static GatewayAuthenticationMethodDefinition CreateMethod()
    {
        return GatewayAuthenticationMethodDefinition.OidcAuthorizationCode(
            "oidc",
            "OIDC",
            "https://accounts.example.test",
            "gateway-client",
            "openid profile email");
    }

    private static HttpMessageHandler CreateDiscoveryOnlyHandler()
    {
        return new FakeOidcHandler(request =>
        {
            if (request.Method == HttpMethod.Get &&
                request.RequestUri?.AbsoluteUri == "https://accounts.example.test/.well-known/openid-configuration")
            {
                return Json("""
{
  "issuer": "https://accounts.example.test",
  "authorization_endpoint": "https://accounts.example.test/authorize",
  "token_endpoint": "https://accounts.example.test/token",
  "jwks_uri": "https://accounts.example.test/jwks"
}
""");
            }

            if (request.Method == HttpMethod.Get &&
                request.RequestUri?.AbsoluteUri == "https://accounts.example.test/jwks")
            {
                return Json("""
{
  "keys": [
    {
      "kty": "RSA",
      "use": "sig",
      "kid": "test-key",
      "alg": "RS256",
      "n": "sXchE1khrct4kQld0M4rZ1m46dD7Zn64iU7cZ2hDbE-Dg7JmEozmVLK3Qa1zWW4l0pXgYOLH2g5B53uTjbAVmw",
      "e": "AQAB"
    }
  ]
}
""");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
    }

    private static async Task<TestClientLease> CreateClientAsync()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
        var accepted = await listener.AcceptTcpClientAsync();
        listener.Stop();
        var serverStream = accepted.GetStream();
        return new TestClientLease(
            new Client(serverStream, serverStream, NullLogger.Instance),
            client,
            accepted);
    }

    private static string CreateIdToken(RSA rsa, string nonce)
    {
        var key = new RsaSecurityKey(rsa)
        {
            KeyId = "test-key"
        };
        var credentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256);
        var token = new JwtSecurityToken(
            issuer: "https://accounts.example.test",
            audience: "gateway-client",
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, "player-oidc"),
                new Claim(JwtRegisteredClaimNames.Nonce, nonce)
            ],
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string CreateJwks(RSA rsa)
    {
        var parameters = rsa.ExportParameters(false);
        return $$"""
{
  "keys": [
    {
      "kty": "RSA",
      "use": "sig",
      "kid": "test-key",
      "alg": "RS256",
      "n": "{{Base64UrlEncoder.Encode(parameters.Modulus)}}",
      "e": "{{Base64UrlEncoder.Encode(parameters.Exponent)}}"
    }
  ]
}
""";
    }

    private static HttpResponseMessage Json(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class StaticHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return client;
        }
    }

    private sealed class FakeOidcHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(handler(request));
        }
    }

    private sealed class TestClientLease(
        Client client,
        TcpClient clientSocket,
        TcpClient serverSocket) : IAsyncDisposable
    {
        public Client Client { get; } = client;

        public async ValueTask DisposeAsync()
        {
            await Client.DisposeAsync();
            clientSocket.Dispose();
            serverSocket.Dispose();
        }
    }
}
