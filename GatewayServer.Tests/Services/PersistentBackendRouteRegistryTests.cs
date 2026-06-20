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
            RouteLifetimeMilliseconds = 10000,
            ExchangeTimeoutMilliseconds = 7000,
            MaxPendingExchangesPerRoute = 8,
            MaxPendingExchangesPerRoutePerDirection = 4,
            RouteOpenRateLimitWindowMilliseconds = 2000,
            MaxRouteOpenAttemptsPerClientPerWindow = 3,
            ExchangeRateLimitWindowMilliseconds = 3000,
            MaxClientOriginExchangeCreatesPerRoutePerWindow = 5,
            MaxBackendOriginExchangeCreatesPerRoutePerWindow = 6
        });
        var firstOwner = new object();
        var secondOwner = new object();
        var alphaRoute = registry.Open(CreateBinding(" alpha "), firstOwner, shutdown.Token);
        var betaRoute = registry.Open(CreateBinding("beta", nodeId: "backend-b", masterConnectionId: "master-b", directConnectionId: "direct-b"), secondOwner, shutdown.Token);

        try
        {
            Assert.True(registry.TryGet(alphaRoute.RouteToken, out var persistentAlpha));
            Assert.Same(firstOwner, persistentAlpha.Owner);
            Assert.Equal("backend-a", persistentAlpha.BackendBinding.NodeId);
            Assert.Equal("master-a", persistentAlpha.BackendBinding.MasterConnectionId);
            Assert.Equal("direct-a", persistentAlpha.BackendBinding.DirectConnectionId);
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
                item.Group == "Persistent Backend routes" &&
                item.Name == "Exchange timeout" &&
                item.Value == "7000 ms");
            Assert.Contains(status, item =>
                item.Group == "Persistent Backend routes" &&
                item.Name == "Max pending exchanges per route" &&
                item.Value == "8");
            Assert.Contains(status, item =>
                item.Group == "Persistent Backend routes" &&
                item.Name == "Max pending exchanges per route direction" &&
                item.Value == "4");
            Assert.Contains(status, item =>
                item.Group == "Persistent Backend routes" &&
                item.Name == "Route-open rate limit" &&
                item.Value == "3 per 2000 ms");
            Assert.Contains(status, item =>
                item.Group == "Persistent Backend routes" &&
                item.Name == "Client-origin exchange rate limit" &&
                item.Value == "5 per 3000 ms");
            Assert.Contains(status, item =>
                item.Group == "Persistent Backend routes" &&
                item.Name == "Backend-origin exchange rate limit" &&
                item.Value == "6 per 3000 ms");
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
            registry.Open(CreateBinding("beta"), new object(), CancellationToken.None));
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
        var route = registry.Open(CreateBinding("alpha"), new object(), shutdown.Token);

        try
        {
            Assert.Throws<InvalidOperationException>(() =>
                registry.Open(CreateBinding("alpha", nodeId: "backend-b", masterConnectionId: "master-b", directConnectionId: "direct-b"), new object(), shutdown.Token));
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
        var firstRoute = registry.Open(CreateBinding("alpha"), owner, shutdown.Token);
        PersistentBackendRoute<object>? otherRoute = null;

        try
        {
            Assert.Throws<InvalidOperationException>(() =>
                registry.Open(CreateBinding("alpha", nodeId: "backend-b", masterConnectionId: "master-b", directConnectionId: "direct-b"), owner, shutdown.Token));

            otherRoute = registry.Open(CreateBinding("alpha", nodeId: "backend-b", masterConnectionId: "master-b", directConnectionId: "direct-b"), otherOwner, shutdown.Token);
            Assert.True(registry.TryGet(otherRoute.RouteToken, out _));
        }
        finally
        {
            registry.CancelAll();
            await WaitForRouteTasksAsync(firstRoute, otherRoute);
        }
    }

    [Fact]
    public void RequireOpenAttemptAllowed_EnforcesPerOwnerRateLimit()
    {
        var registry = CreateRegistry(new BackendRouteOptions
        {
            AllowedBackendKinds = ["alpha"],
            RouteOpenRateLimitWindowMilliseconds = 10000,
            MaxRouteOpenAttemptsPerClientPerWindow = 2
        });
        var owner = new object();
        var otherOwner = new object();

        registry.RequireOpenAttemptAllowed(owner);
        registry.RequireOpenAttemptAllowed(owner);

        Assert.Throws<InvalidOperationException>(() =>
            registry.RequireOpenAttemptAllowed(owner));

        registry.RequireOpenAttemptAllowed(otherOwner);
        Assert.Equal(0, registry.RemoveOwnerRoutes(owner, "client disconnected"));
        registry.RequireOpenAttemptAllowed(owner);
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
        var route = registry.Open(CreateBinding("alpha"), new object(), shutdown.Token);

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
        var firstRoute = registry.Open(CreateBinding("alpha"), owner, shutdown.Token);
        var secondRoute = registry.Open(CreateBinding("alpha", nodeId: "backend-b", masterConnectionId: "master-b", directConnectionId: "direct-b"), owner, shutdown.Token);
        var otherRoute = registry.Open(CreateBinding("alpha", nodeId: "backend-c", masterConnectionId: "master-c", directConnectionId: "direct-c"), otherOwner, shutdown.Token);

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
    public async Task RemoveBackendBindingRoutes_RemovesOnlyExactBackendSession()
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
        var binding = CreateBinding("alpha");
        var sameBindingRoute = registry.Open(binding, owner, shutdown.Token);
        var equalBindingRoute = registry.Open(CreateBinding("alpha"), owner, shutdown.Token);
        var otherSessionRoute = registry.Open(
            CreateBinding("alpha", nodeId: "backend-b", masterConnectionId: "master-b", directConnectionId: "direct-b"),
            owner,
            shutdown.Token);

        try
        {
            var removed = registry.RemoveBackendBindingRoutes(binding, "backend disconnected");

            Assert.Equal(2, removed.Length);
            Assert.Contains(sameBindingRoute, removed);
            Assert.Contains(equalBindingRoute, removed);
            Assert.DoesNotContain(otherSessionRoute, removed);
            Assert.False(registry.TryGet(sameBindingRoute.RouteToken, out _));
            Assert.False(registry.TryGet(equalBindingRoute.RouteToken, out _));
            Assert.True(registry.TryGet(otherSessionRoute.RouteToken, out _));
            Assert.Equal(PersistentBackendRouteState.Closed, sameBindingRoute.State);
            Assert.Equal(PersistentBackendRouteState.Closed, equalBindingRoute.State);
            Assert.Equal(PersistentBackendRouteState.Open, otherSessionRoute.State);
        }
        finally
        {
            registry.CancelAll();
            await WaitForRouteTasksAsync(sameBindingRoute, equalBindingRoute, otherSessionRoute);
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
        var route = registry.Open(CreateBinding("alpha"), new object(), shutdown.Token);

        await WaitForRouteTasksAsync(route);

        Assert.False(registry.TryGet(route.RouteToken, out _));
        Assert.Equal(PersistentBackendRouteState.Closed, route.State);
        Assert.Equal("expired", route.CloseReason);
    }

    [Fact]
    public async Task Route_EnforcesPendingExchangeCapacity()
    {
        using var shutdown = new CancellationTokenSource();
        var registry = CreateRegistry(new BackendRouteOptions
        {
            AllowedBackendKinds = ["alpha"],
            RouteLifetimeMilliseconds = 10000,
            MaxPendingExchangesPerRoute = 1,
            MaxPendingExchangesPerRoutePerDirection = 0
        });
        var route = registry.Open(CreateBinding("alpha"), new object(), shutdown.Token);

        try
        {
            var exchangeId = new GatewayBackendExchangeId(Guid.NewGuid());
            route.RegisterClientOriginExchange(exchangeId, requestPacketId: 401, requestVersion: 3);

            Assert.Throws<InvalidOperationException>(() =>
                route.RegisterBackendOriginExchange(
                    new GatewayBackendExchangeId(Guid.NewGuid()),
                    requestPacketId: 601,
                    requestVersion: 4));
            Assert.Equal(1, route.PendingClientExchangeCount);
            Assert.Equal(0, route.PendingBackendExchangeCount);
        }
        finally
        {
            registry.CancelAll();
            await WaitForRouteTasksAsync(route);
        }
    }

    [Fact]
    public async Task Route_EnforcesPendingExchangeCapacityPerDirection()
    {
        using var shutdown = new CancellationTokenSource();
        var registry = CreateRegistry(new BackendRouteOptions
        {
            AllowedBackendKinds = ["alpha"],
            RouteLifetimeMilliseconds = 10000,
            MaxPendingExchangesPerRoute = 0,
            MaxPendingExchangesPerRoutePerDirection = 1
        });
        var route = registry.Open(CreateBinding("alpha"), new object(), shutdown.Token);

        try
        {
            route.RegisterClientOriginExchange(
                new GatewayBackendExchangeId(Guid.NewGuid()),
                requestPacketId: 401,
                requestVersion: 3);

            Assert.Throws<InvalidOperationException>(() =>
                route.RegisterClientOriginExchange(
                    new GatewayBackendExchangeId(Guid.NewGuid()),
                    requestPacketId: 402,
                    requestVersion: 3));

            route.RegisterBackendOriginExchange(
                new GatewayBackendExchangeId(Guid.NewGuid()),
                requestPacketId: 601,
                requestVersion: 4);
            Assert.Equal(1, route.PendingClientExchangeCount);
            Assert.Equal(1, route.PendingBackendExchangeCount);
        }
        finally
        {
            registry.CancelAll();
            await WaitForRouteTasksAsync(route);
        }
    }

    [Fact]
    public async Task Route_EnforcesExchangeCreationRateLimitsPerDirection()
    {
        using var shutdown = new CancellationTokenSource();
        var registry = CreateRegistry(new BackendRouteOptions
        {
            AllowedBackendKinds = ["alpha"],
            RouteLifetimeMilliseconds = 10000,
            ExchangeRateLimitWindowMilliseconds = 10000,
            MaxClientOriginExchangeCreatesPerRoutePerWindow = 1,
            MaxBackendOriginExchangeCreatesPerRoutePerWindow = 1
        });
        var route = registry.Open(CreateBinding("alpha"), new object(), shutdown.Token);

        try
        {
            route.RegisterClientOriginExchange(
                new GatewayBackendExchangeId(Guid.NewGuid()),
                requestPacketId: 401,
                requestVersion: 3);
            Assert.Throws<InvalidOperationException>(() =>
                route.RegisterClientOriginExchange(
                    new GatewayBackendExchangeId(Guid.NewGuid()),
                    requestPacketId: 402,
                    requestVersion: 3));

            route.RegisterBackendOriginExchange(
                new GatewayBackendExchangeId(Guid.NewGuid()),
                requestPacketId: 601,
                requestVersion: 4);
            Assert.Throws<InvalidOperationException>(() =>
                route.RegisterBackendOriginExchange(
                    new GatewayBackendExchangeId(Guid.NewGuid()),
                    requestPacketId: 602,
                    requestVersion: 4));
        }
        finally
        {
            registry.CancelAll();
            await WaitForRouteTasksAsync(route);
        }
    }

    [Fact]
    public async Task Route_ExpiresPendingExchangesWithoutClosingRoute()
    {
        using var shutdown = new CancellationTokenSource();
        var registry = CreateRegistry(new BackendRouteOptions
        {
            AllowedBackendKinds = ["alpha"],
            RouteLifetimeMilliseconds = 10000,
            ExchangeTimeoutMilliseconds = 25,
            MaxPendingExchangesPerRoute = 1,
            MaxPendingExchangesPerRoutePerDirection = 1
        });
        var route = registry.Open(CreateBinding("alpha"), new object(), shutdown.Token);

        try
        {
            var clientExchangeId = new GatewayBackendExchangeId(Guid.NewGuid());
            var backendExchangeId = new GatewayBackendExchangeId(Guid.NewGuid());
            route.RegisterClientOriginExchange(clientExchangeId, requestPacketId: 401, requestVersion: 3);
            await Task.Delay(TimeSpan.FromMilliseconds(75));

            Assert.False(route.ContainsClientOriginExchange(clientExchangeId));
            Assert.Equal(0, route.PendingClientExchangeCount);
            Assert.Equal(PersistentBackendRouteState.Open, route.State);
            Assert.True(registry.TryGet(route.RouteToken, out _));

            route.RegisterBackendOriginExchange(backendExchangeId, requestPacketId: 601, requestVersion: 4);
            await Task.Delay(TimeSpan.FromMilliseconds(75));

            Assert.False(route.ContainsBackendOriginExchange(backendExchangeId));
            Assert.Equal(0, route.PendingBackendExchangeCount);
            Assert.Equal(PersistentBackendRouteState.Open, route.State);
            Assert.True(registry.TryGet(route.RouteToken, out _));
        }
        finally
        {
            registry.CancelAll();
            await WaitForRouteTasksAsync(route);
        }
    }

    private static PersistentBackendRouteRegistry<object> CreateRegistry(BackendRouteOptions options)
    {
        return new PersistentBackendRouteRegistry<object>(
            options,
            new GatewayBackendRouteTokenGenerator(),
            NullLogger.Instance);
    }

    private static BackendRouteBinding CreateBinding(
        string backendKind,
        string nodeId = "backend-a",
        string masterConnectionId = "master-a",
        string directConnectionId = "direct-a")
    {
        return new BackendRouteBinding(
            backendKind,
            nodeId,
            masterConnectionId,
            directConnectionId);
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
