using System.Security.Cryptography;
using System.Text;
using GatewayServer.Services;
using MasterServer.ControlPlane;
using MasterServer.Services;
using Xunit;

namespace GatewayServer.Tests.Services;

public sealed class GatewayClientSecretCredentialCatalogTests
{
    [Fact]
    public async Task ValidateAsync_DefaultsToDenied()
    {
        var catalog = new GatewayClientSecretCredentialCatalog();

        var result = await catalog.ValidateAsync("gwc_token-a.secret-alpha", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("Gateway client secret credentials are not configured.", result.ErrorMessage);
    }

    [Fact]
    public async Task ValidateAsync_AcceptsPublishedSecret()
    {
        var catalog = new GatewayClientSecretCredentialCatalog();
        catalog.Publish(new GatewayClientSecretCredentialSnapshot(
            [CreateSecret("token-a", "player-1", "secret-alpha")],
            DateTimeOffset.UtcNow));

        var accepted = await catalog.ValidateAsync("gwc_token-a.secret-alpha", CancellationToken.None);
        var rejected = await catalog.ValidateAsync("gwc_token-a.secret-beta", CancellationToken.None);

        Assert.True(accepted.Success);
        Assert.NotNull(accepted.Principal);
        Assert.Equal("player-1", accepted.Principal.SubjectId);
        Assert.False(rejected.Success);
        Assert.Equal("Invalid Gateway client access token.", rejected.ErrorMessage);
    }

    [Fact]
    public async Task Publish_ReplacesPreviousSecrets()
    {
        var catalog = new GatewayClientSecretCredentialCatalog();
        catalog.Publish(new GatewayClientSecretCredentialSnapshot(
            [CreateSecret("token-a", "player-1", "secret-alpha")],
            DateTimeOffset.UtcNow));
        catalog.Publish(new GatewayClientSecretCredentialSnapshot(
            [CreateSecret("token-b", "player-2", "secret-beta")],
            DateTimeOffset.UtcNow));

        var oldResult = await catalog.ValidateAsync("gwc_token-a.secret-alpha", CancellationToken.None);
        var newResult = await catalog.ValidateAsync("gwc_token-b.secret-beta", CancellationToken.None);

        Assert.False(oldResult.Success);
        Assert.True(newResult.Success);
        Assert.NotNull(newResult.Principal);
        Assert.Equal("player-2", newResult.Principal.SubjectId);
    }

    private static GatewayClientSecretValidationInfo CreateSecret(
        string tokenId,
        string subjectId,
        string secret)
    {
        return new GatewayClientSecretValidationInfo(
            tokenId,
            subjectId,
            Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(secret))));
    }
}
