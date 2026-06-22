using GatewayServer.Services;
using MasterServer.ControlPlane;
using Xunit;

namespace GatewayServer.Tests.Services;

public sealed class GatewayBackendRoutePolicyCatalogTests
{
    [Fact]
    public void IsBackendKindAllowed_DefaultsToDenied()
    {
        var catalog = new GatewayBackendRoutePolicyCatalog();

        bool allowed = catalog.IsBackendKindAllowed("alpha", out var normalizedBackendKind);

        Assert.False(allowed);
        Assert.Equal("alpha", normalizedBackendKind);
        Assert.Empty(catalog.GetAllowedBackendKinds());
    }

    [Fact]
    public void Publish_ReplacesAllowedBackendKinds()
    {
        var catalog = new GatewayBackendRoutePolicyCatalog();
        catalog.Publish(new GatewayBackendRoutePolicySnapshot(
            [" beta ", "alpha", "alpha", "", "  "],
            DateTimeOffset.UtcNow));

        Assert.True(catalog.IsBackendKindAllowed("alpha", out var alphaKind));
        Assert.True(catalog.IsBackendKindAllowed(" beta ", out var betaKind));
        Assert.False(catalog.IsBackendKindAllowed("gamma", out var gammaKind));
        Assert.Equal("alpha", alphaKind);
        Assert.Equal("beta", betaKind);
        Assert.Equal("gamma", gammaKind);
        Assert.Equal(["alpha", "beta"], catalog.GetAllowedBackendKinds());

        catalog.Publish(new GatewayBackendRoutePolicySnapshot(["gamma"], DateTimeOffset.UtcNow));

        Assert.False(catalog.IsBackendKindAllowed("alpha", out _));
        Assert.True(catalog.IsBackendKindAllowed("gamma", out var updatedKind));
        Assert.Equal("gamma", updatedKind);
    }
}
