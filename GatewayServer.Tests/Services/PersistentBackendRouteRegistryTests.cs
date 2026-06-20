using GatewayServer.Options;
using GatewayServer.Protocols;
using GatewayServer.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GatewayServer.Tests.Services;

public sealed class PersistentBackendRouteRegistryTests
{
    [Fact]
    public void TokenGenerator_CreatesOpaqueTransportSafeTokens()
    {
        var generator = new GatewayBackendRouteTokenGenerator();

        var first = generator.Generate();
        var second = generator.Generate();

        Assert.NotEqual(first, second);
        Assert.DoesNotContain("+", first.Value, StringComparison.Ordinal);
        Assert.DoesNotContain("/", first.Value, StringComparison.Ordinal);
        Assert.DoesNotContain("=", first.Value, StringComparison.Ordinal);
        Assert.DoesNotContain(" ", first.Value, StringComparison.Ordinal);
        Assert.True(first.Value.Length >= 32);
    }

    [Fact]
    public void TokenGenerator_FingerprintDoesNotExposeFullToken()
    {
        var generator = new GatewayBackendRouteTokenGenerator();
        var token = new GatewayBackendRouteToken("route-token-alpha");

        var first = generator.GetFingerprint(token);
        var second = generator.GetFingerprint(token);

        Assert.Equal(first, second);
        Assert.Equal(12, first.Length);
        Assert.DoesNotContain(token.Value, first, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Open_AddsPersistentRoutesAndStatusGroups()
    {
        using var shutdown = new CancellationTokenSource();
        var registry = CreateRegistry(new BackendRouteOptions
        {
            AllowedBackendKinds = ["beta", "alpha"],
            MaxOpenRoutes = 4,
            MaxOpenRoutesPerClient = 2,
            RouteLifetimeMilliseconds = 10000
        });
        var firstOwner = new object();
        var secondOwner = new object();
        var alphaRoute = registry.Open(" alpha ", firstOwner, shutdown.Token);
        var betaRoute = registry.Open("beta", secondOwner, shutdown.Token);

        try
        {
            Assert.True(registry.TryGet(alphaRoute.RouteToken, out var persistentAlpha));
            Assert.Same(firstOwner, persistentAlpha.Owner);
            Assert.Equal("alpha", persistentAlpha.BackendKind);
            Assert.Equal(PersistentBackendRouteState.Open, persistentAlpha.State);

            var status = registry.GetStatusItems();
            Assert.Contains(status, item =>
                item.Group == "Persistent Backend routes" &&
                item.Name == "Allowed kinds" &&
                item.Value == "alpha, beta");
            Assert.Contains(status, item =>
                item.Group == "Persistent Backend routes" &&
                item.Name == "Open routes" &&
                item.Value == "2");
            Assert.Contains(status, item =>
                item.Group == "Persistent Backend routes" &&
                item.Name == "Route lifetime" &&
                item.Value == "10000 ms");
            Assert.Contains(status, item =>
                item.Group == "Persistent Backend route alpha" &&
                item.Name == "Open routes" &&
                item.Value == "1");
            Assert.Contains(status, item =>
                item.Group == "Persistent Backend route beta" &&
                item.Name == "Open routes" &&
                item.Value == "1");
        }
        finally
        {
            registry.CancelAll();
            await WaitForRouteTasksAsync(alphaRoute, betaRoute);
        }
    }

    [Fact]
    public void Open_RejectsDisallowedBackendKind()
    {
        var registry = CreateRegistry(new BackendRouteOptions
        {
            AllowedBackendKinds = ["alpha"]
        });

        Assert.Equal("alpha", registry.RequireAllowedBackendKind(" alpha "));
        Assert.Throws<UnauthorizedAccessException>(() =>
            registry.Open("beta", new object(), CancellationToken.None));
    }

    [Fact]
    public async Task Open_EnforcesGlobalCapacity()
    {
        using var shutdown = new CancellationTokenSource();
        var registry = CreateRegistry(new BackendRouteOptions
        {
            AllowedBackendKinds = ["alpha"],
            MaxOpenRoutes = 1,
            MaxOpenRoutesPerClient = 0,
            RouteLifetimeMilliseconds = 10000
        });
        var route = registry.Open("alpha", new object(), shutdown.Token);

        try
        {
            Assert.Throws<InvalidOperationException>(() =>
                registry.Open("alpha", new object(), shutdown.Token));
        }
        finally
        {
            registry.CancelAll();
            await WaitForRouteTasksAsync(route);
        }
    }

    [Fact]
    public async Task Open_EnforcesOwnerCapacity()
    {
        using var shutdown = new CancellationTokenSource();
        var registry = CreateRegistry(new BackendRouteOptions
        {
            AllowedBackendKinds = ["alpha"],
            MaxOpenRoutes = 0,
            MaxOpenRoutesPerClient = 1,
            RouteLifetimeMilliseconds = 10000
        });
        var owner = new object();
        var otherOwner = new object();
        var firstRoute = registry.Open("alpha", owner, shutdown.Token);
        PersistentBackendRoute<object>? otherRoute = null;

        try
        {
            Assert.Throws<InvalidOperationException>(() =>
                registry.Open("alpha", owner, shutdown.Token));

            otherRoute = registry.Open("alpha", otherOwner, shutdown.Token);
            Assert.True(registry.TryGet(otherRoute.RouteToken, out _));
        }
        finally
        {
            registry.CancelAll();
            await WaitForRouteTasksAsync(firstRoute, otherRoute);
        }
    }

    [Fact]
    public async Task Close_RemovesRouteAndMarksClosed()
    {
        using var shutdown = new CancellationTokenSource();
        var registry = CreateRegistry(new BackendRouteOptions
        {
            AllowedBackendKinds = ["alpha"],
            RouteLifetimeMilliseconds = 10000
        });
        var route = registry.Open("alpha", new object(), shutdown.Token);

        Assert.True(registry.Close(route.RouteToken, "client closed"));
        Assert.False(registry.TryGet(route.RouteToken, out _));
        Assert.False(registry.Close(route.RouteToken, "client closed"));
        Assert.Equal(PersistentBackendRouteState.Closed, route.State);
        Assert.Equal("client closed", route.CloseReason);

        await WaitForRouteTasksAsync(route);
    }

    [Fact]
    public async Task RemoveOwnerRoutes_RemovesOnlyMatchingOwner()
    {
        using var shutdown = new CancellationTokenSource();
        var registry = CreateRegistry(new BackendRouteOptions
        {
            AllowedBackendKinds = ["alpha"],
            MaxOpenRoutes = 0,
            MaxOpenRoutesPerClient = 0,
            RouteLifetimeMilliseconds = 10000
        });
        var owner = new object();
        var otherOwner = new object();
        var firstRoute = registry.Open("alpha", owner, shutdown.Token);
        var secondRoute = registry.Open("alpha", owner, shutdown.Token);
        var otherRoute = registry.Open("alpha", otherOwner, shutdown.Token);

        try
        {
            Assert.Equal(2, registry.RemoveOwnerRoutes(owner, "client disconnected"));
            Assert.False(registry.TryGet(firstRoute.RouteToken, out _));
            Assert.False(registry.TryGet(secondRoute.RouteToken, out _));
            Assert.True(registry.TryGet(otherRoute.RouteToken, out _));
            Assert.Equal(PersistentBackendRouteState.Closed, firstRoute.State);
            Assert.Equal(PersistentBackendRouteState.Closed, secondRoute.State);
            Assert.Equal(PersistentBackendRouteState.Open, otherRoute.State);
        }
        finally
        {
            registry.CancelAll();
            await WaitForRouteTasksAsync(firstRoute, secondRoute, otherRoute);
        }
    }

    [Fact]
    public async Task Route_ExpiresAfterLifetime()
    {
        using var shutdown = new CancellationTokenSource();
        var registry = CreateRegistry(new BackendRouteOptions
        {
            AllowedBackendKinds = ["alpha"],
            RouteLifetimeMilliseconds = 25
        });
        var route = registry.Open("alpha", new object(), shutdown.Token);

        await WaitForRouteTasksAsync(route);

        Assert.False(registry.TryGet(route.RouteToken, out _));
        Assert.Equal(PersistentBackendRouteState.Closed, route.State);
        Assert.Equal("expired", route.CloseReason);
    }

    private static PersistentBackendRouteRegistry<object> CreateRegistry(BackendRouteOptions options)
    {
        return new PersistentBackendRouteRegistry<object>(
            options,
            new GatewayBackendRouteTokenGenerator(),
            NullLogger.Instance);
    }

    private static async Task WaitForRouteTasksAsync(params PersistentBackendRoute<object>?[] routes)
    {
        foreach (var route in routes)
        {
            if (route?.TimeoutTask != null)
            {
                await route.TimeoutTask.ConfigureAwait(false);
            }
        }
    }
}
