using GatewayServer.Options;
using GatewayServer.Services;
using Microsoft.Extensions.Logging.Abstractions;
using PacketCore;
using Xunit;

namespace GatewayServer.Tests.Services;

public sealed class BackendRouteRegistryTests
{
    [Fact]
    public void RequireAllowedBackendKind_UsesTrimmedOrdinalBackendKind()
    {
        var registry = CreateRegistry(new BackendRouteOptions
        {
            AllowedBackendKinds = [" alpha "]
        });

        Assert.Equal("alpha", registry.RequireAllowedBackendKind(" alpha "));
        Assert.Throws<UnauthorizedAccessException>(() => registry.RequireAllowedBackendKind("Alpha"));
        Assert.Throws<UnauthorizedAccessException>(() => registry.RequireAllowedBackendKind("beta"));
    }

    [Fact]
    public async Task Register_AddsPendingRoutesAndStatusGroups()
    {
        using var shutdown = new CancellationTokenSource();
        var registry = CreateRegistry(new BackendRouteOptions
        {
            AllowedBackendKinds = ["beta", "alpha"],
            EnableLegacyOneShotRoutes = true,
            MaxPendingRoutes = 4,
            MaxPendingRoutesPerClient = 2,
            RequestTimeoutMilliseconds = 500
        });
        var firstOwner = new object();
        var secondOwner = new object();
        var alphaRoute = registry.Register(Guid.NewGuid(), " alpha ", firstOwner, shutdown.Token);
        var betaRoute = registry.Register(Guid.NewGuid(), "beta", secondOwner, shutdown.Token);

        try
        {
            Assert.True(registry.TryGet(alphaRoute.RouteId, out var pendingAlpha));
            Assert.Same(firstOwner, pendingAlpha.Owner);
            Assert.Equal("alpha", pendingAlpha.BackendKind);

            var status = registry.GetStatusItems();
            Assert.Contains(status, item =>
                item.Group == "Backend routes" &&
                item.Name == "Allowed kinds" &&
                item.Value == "alpha, beta");
            Assert.Contains(status, item =>
                item.Group == "Backend routes" &&
                item.Name == "Legacy one-shot routes" &&
                item.Value == "Enabled");
            Assert.Contains(status, item =>
                item.Group == "Backend routes" &&
                item.Name == "Legacy one-shot packets" &&
                item.Value == "0");
            Assert.Contains(status, item =>
                item.Group == "Backend routes" &&
                item.Name == "Pending routes" &&
                item.Value == "2");
            Assert.Contains(status, item =>
                item.Group == "Backend routes" &&
                item.Name == "Request timeout" &&
                item.Value == "1000 ms");
            Assert.Contains(status, item =>
                item.Group == "Backend route alpha" &&
                item.Name == "Pending routes" &&
                item.Value == "1");
            Assert.Contains(status, item =>
                item.Group == "Backend route beta" &&
                item.Name == "Pending routes" &&
                item.Value == "1");
        }
        finally
        {
            registry.CancelAll();
            await WaitForRouteTasksAsync(alphaRoute, betaRoute);
        }
    }

    [Fact]
    public void RecordLegacyRoutePacket_UpdatesStatusCounters()
    {
        var registry = CreateRegistry(new BackendRouteOptions
        {
            AllowedBackendKinds = ["alpha"]
        });

        registry.RecordLegacyRoutePacket(PacketKind.Request);
        registry.RecordLegacyRoutePacket(PacketKind.Notify);
        registry.RecordLegacyRoutePacket(PacketKind.Response);

        var status = registry.GetStatusItems();
        Assert.Contains(status, item =>
            item.Group == "Backend routes" &&
            item.Name == "Legacy one-shot packets" &&
            item.Value == "3");
        Assert.Contains(status, item =>
            item.Group == "Backend routes" &&
            item.Name == "Legacy one-shot requests" &&
            item.Value == "1");
        Assert.Contains(status, item =>
            item.Group == "Backend routes" &&
            item.Name == "Legacy one-shot notifies" &&
            item.Value == "1");
    }

    [Fact]
    public void GetStatusItems_ReportsLegacyOneShotRoutesDisabledByDefault()
    {
        var registry = CreateRegistry(new BackendRouteOptions
        {
            AllowedBackendKinds = ["alpha"]
        });

        Assert.Contains(registry.GetStatusItems(), item =>
            item.Group == "Backend routes" &&
            item.Name == "Legacy one-shot routes" &&
            item.Value == "Disabled");
    }

    [Fact]
    public async Task Register_RejectsDuplicateRouteId()
    {
        using var shutdown = new CancellationTokenSource();
        var registry = CreateRegistry(new BackendRouteOptions
        {
            AllowedBackendKinds = ["alpha"],
            MaxPendingRoutes = 0,
            MaxPendingRoutesPerClient = 0
        });
        var owner = new object();
        var routeId = Guid.NewGuid();
        var route = registry.Register(routeId, "alpha", owner, shutdown.Token);

        try
        {
            Assert.Throws<InvalidOperationException>(() =>
                registry.Register(routeId, "alpha", owner, shutdown.Token));
        }
        finally
        {
            registry.CancelAll();
            await WaitForRouteTasksAsync(route);
        }
    }

    [Fact]
    public async Task Register_EnforcesGlobalCapacity()
    {
        using var shutdown = new CancellationTokenSource();
        var registry = CreateRegistry(new BackendRouteOptions
        {
            AllowedBackendKinds = ["alpha"],
            MaxPendingRoutes = 1,
            MaxPendingRoutesPerClient = 0
        });
        var firstRoute = registry.Register(Guid.NewGuid(), "alpha", new object(), shutdown.Token);

        try
        {
            Assert.Throws<InvalidOperationException>(() =>
                registry.Register(Guid.NewGuid(), "alpha", new object(), shutdown.Token));
        }
        finally
        {
            registry.CancelAll();
            await WaitForRouteTasksAsync(firstRoute);
        }
    }

    [Fact]
    public async Task Register_EnforcesOwnerCapacity()
    {
        using var shutdown = new CancellationTokenSource();
        var registry = CreateRegistry(new BackendRouteOptions
        {
            AllowedBackendKinds = ["alpha"],
            MaxPendingRoutes = 0,
            MaxPendingRoutesPerClient = 1
        });
        var owner = new object();
        var otherOwner = new object();
        var firstRoute = registry.Register(Guid.NewGuid(), "alpha", owner, shutdown.Token);
        PendingBackendRoute<object>? otherRoute = null;

        try
        {
            Assert.Throws<InvalidOperationException>(() =>
                registry.Register(Guid.NewGuid(), "alpha", owner, shutdown.Token));

            otherRoute = registry.Register(Guid.NewGuid(), "alpha", otherOwner, shutdown.Token);
            Assert.True(registry.TryGet(otherRoute.RouteId, out _));
        }
        finally
        {
            registry.CancelAll();
            await WaitForRouteTasksAsync(firstRoute, otherRoute);
        }
    }

    [Fact]
    public async Task RemoveOwnerRoutes_RemovesOnlyMatchingOwner()
    {
        using var shutdown = new CancellationTokenSource();
        var registry = CreateRegistry(new BackendRouteOptions
        {
            AllowedBackendKinds = ["alpha"],
            MaxPendingRoutes = 0,
            MaxPendingRoutesPerClient = 0
        });
        var owner = new object();
        var otherOwner = new object();
        var firstRoute = registry.Register(Guid.NewGuid(), "alpha", owner, shutdown.Token);
        var secondRoute = registry.Register(Guid.NewGuid(), "alpha", owner, shutdown.Token);
        var otherRoute = registry.Register(Guid.NewGuid(), "alpha", otherOwner, shutdown.Token);

        try
        {
            Assert.Equal(2, registry.RemoveOwnerRoutes(owner));
            Assert.False(registry.TryGet(firstRoute.RouteId, out _));
            Assert.False(registry.TryGet(secondRoute.RouteId, out _));
            Assert.True(registry.TryGet(otherRoute.RouteId, out _));
        }
        finally
        {
            registry.CancelAll();
            await WaitForRouteTasksAsync(firstRoute, secondRoute, otherRoute);
        }
    }

    [Fact]
    public async Task Remove_RemovesPendingRoute()
    {
        using var shutdown = new CancellationTokenSource();
        var registry = CreateRegistry(new BackendRouteOptions
        {
            AllowedBackendKinds = ["alpha"],
            MaxPendingRoutes = 0,
            MaxPendingRoutesPerClient = 0
        });
        var route = registry.Register(Guid.NewGuid(), "alpha", new object(), shutdown.Token);

        Assert.True(registry.Remove(route.RouteId));
        Assert.False(registry.TryGet(route.RouteId, out _));
        Assert.False(registry.Remove(route.RouteId));

        await WaitForRouteTasksAsync(route);
    }

    private static BackendRouteRegistry<object> CreateRegistry(BackendRouteOptions options)
    {
        return new BackendRouteRegistry<object>(options, NullLogger.Instance);
    }

    private static async Task WaitForRouteTasksAsync(params PendingBackendRoute<object>?[] routes)
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
