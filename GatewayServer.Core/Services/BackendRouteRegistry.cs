using System.Collections.Concurrent;
using GatewayServer.Options;
using MasterServer.ControlPlane;
using Microsoft.Extensions.Logging;

namespace GatewayServer.Services;

internal sealed class BackendRouteRegistry<TOwner>(
    BackendRouteOptions options,
    ILogger logger)
    where TOwner : class
{
    private readonly object m_RegistrationSync = new();
    private readonly ConcurrentDictionary<Guid, PendingBackendRoute<TOwner>> m_PendingRoutes = [];

    public string RequireAllowedBackendKind(string backendKind)
    {
        if (string.IsNullOrWhiteSpace(backendKind))
        {
            throw new ArgumentException("Backend kind is required.", nameof(backendKind));
        }

        var normalizedBackendKind = backendKind.Trim();
        var allowedBackendKinds = options.AllowedBackendKinds ?? [];
        if (allowedBackendKinds.Any(candidate => string.Equals(
                candidate?.Trim(),
                normalizedBackendKind,
                StringComparison.Ordinal)))
        {
            return normalizedBackendKind;
        }

        throw new UnauthorizedAccessException($"Backend kind '{normalizedBackendKind}' is not enabled for client routing.");
    }

    public PendingBackendRoute<TOwner> Register(
        Guid routeId,
        string backendKind,
        TOwner owner,
        CancellationToken cancellationToken)
    {
        if (routeId == Guid.Empty)
        {
            throw new ArgumentException("Route id is required.", nameof(routeId));
        }

        if (owner == null)
        {
            throw new ArgumentNullException(nameof(owner));
        }

        var normalizedBackendKind = RequireAllowedBackendKind(backendKind);
        var now = DateTimeOffset.UtcNow;
        var pendingRoute = new PendingBackendRoute<TOwner>(
            routeId,
            normalizedBackendKind,
            owner,
            now,
            now.AddMilliseconds(GetBackendRouteTimeoutMilliseconds()),
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken));

        lock (m_RegistrationSync)
        {
            EnsureBackendRouteCapacity(owner);
            if (!m_PendingRoutes.TryAdd(routeId, pendingRoute))
            {
                pendingRoute.TimeoutCancellation.Cancel();
                pendingRoute.TimeoutCancellation.Dispose();
                throw new InvalidOperationException("Backend route id is already active.");
            }
        }

        pendingRoute.TimeoutTask = ExpireBackendRouteAsync(pendingRoute);
        return pendingRoute;
    }

    public bool TryGet(
        Guid routeId,
        out PendingBackendRoute<TOwner> pendingRoute)
    {
        return m_PendingRoutes.TryGetValue(routeId, out pendingRoute!);
    }

    public bool Remove(Guid routeId)
    {
        if (!m_PendingRoutes.TryRemove(routeId, out var pendingRoute))
        {
            return false;
        }

        pendingRoute.TimeoutCancellation.Cancel();
        return true;
    }

    public int RemoveOwnerRoutes(TOwner owner)
    {
        var removed = 0;
        foreach (var pair in m_PendingRoutes.ToArray())
        {
            if (ReferenceEquals(pair.Value.Owner, owner) &&
                Remove(pair.Key))
            {
                removed++;
            }
        }

        return removed;
    }

    public void CancelAll()
    {
        foreach (var routeId in m_PendingRoutes.Keys)
        {
            Remove(routeId);
        }
    }

    public ServiceAdminStatusItem[] GetStatusItems()
    {
        var pendingRoutes = m_PendingRoutes.Values.ToArray();
        var allowedBackendKinds = options.AllowedBackendKinds ?? [];
        var items = new List<ServiceAdminStatusItem>
        {
            new("Backend routes", "Allowed kinds", allowedBackendKinds.Length == 0
                ? "None"
                : string.Join(", ", allowedBackendKinds.OrderBy(static item => item, StringComparer.Ordinal))),
            new("Backend routes", "Legacy one-shot routes", options.EnableLegacyOneShotRoutes ? "Enabled" : "Disabled"),
            new("Backend routes", "Pending routes", pendingRoutes.Length.ToString()),
            new("Backend routes", "Request timeout", $"{GetBackendRouteTimeoutMilliseconds()} ms"),
            new("Backend routes", "Max pending routes", FormatLimit(options.MaxPendingRoutes)),
            new("Backend routes", "Max pending routes per client", FormatLimit(options.MaxPendingRoutesPerClient))
        };

        foreach (var group in pendingRoutes
                     .GroupBy(static route => route.BackendKind, StringComparer.Ordinal)
                     .OrderBy(static group => group.Key, StringComparer.Ordinal))
        {
            items.Add(new ServiceAdminStatusItem($"Backend route {group.Key}", "Pending routes", group.Count().ToString()));
        }

        return [.. items];
    }

    private void EnsureBackendRouteCapacity(TOwner owner)
    {
        var maxPendingRoutes = options.MaxPendingRoutes;
        if (maxPendingRoutes > 0 &&
            m_PendingRoutes.Count >= maxPendingRoutes)
        {
            throw new InvalidOperationException("Gateway Backend route capacity is exhausted.");
        }

        var maxPendingRoutesPerClient = options.MaxPendingRoutesPerClient;
        if (maxPendingRoutesPerClient > 0 &&
            m_PendingRoutes.Values.Count(route => ReferenceEquals(route.Owner, owner)) >= maxPendingRoutesPerClient)
        {
            throw new InvalidOperationException("Gateway Backend route capacity is exhausted for this client.");
        }
    }

    private async Task ExpireBackendRouteAsync(PendingBackendRoute<TOwner> pendingRoute)
    {
        try
        {
            var delay = pendingRoute.ExpiresAt - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, pendingRoute.TimeoutCancellation.Token).ConfigureAwait(false);
            }

            if (m_PendingRoutes.TryRemove(pendingRoute.RouteId, out _))
            {
                logger.LogWarning(
                    "Gateway Backend route expired. BackendKind={BackendKind}, RouteId={RouteId}, CreatedAt={CreatedAt:O}, ExpiresAt={ExpiresAt:O}.",
                    pendingRoute.BackendKind,
                    pendingRoute.RouteId,
                    pendingRoute.CreatedAt,
                    pendingRoute.ExpiresAt);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception e)
        {
            logger.LogWarning(
                e,
                "Gateway Backend route expiration task failed. BackendKind={BackendKind}, RouteId={RouteId}.",
                pendingRoute.BackendKind,
                pendingRoute.RouteId);
        }
        finally
        {
            pendingRoute.TimeoutCancellation.Dispose();
        }
    }

    private int GetBackendRouteTimeoutMilliseconds()
    {
        return Math.Max(1000, options.RequestTimeoutMilliseconds);
    }

    private static string FormatLimit(int limit)
    {
        return limit <= 0 ? "Unlimited" : limit.ToString();
    }
}

internal sealed class PendingBackendRoute<TOwner>(
    Guid routeId,
    string backendKind,
    TOwner owner,
    DateTimeOffset createdAt,
    DateTimeOffset expiresAt,
    CancellationTokenSource timeoutCancellation)
    where TOwner : class
{
    public Guid RouteId { get; } = routeId;

    public string BackendKind { get; } = backendKind;

    public TOwner Owner { get; } = owner;

    public DateTimeOffset CreatedAt { get; } = createdAt;

    public DateTimeOffset ExpiresAt { get; } = expiresAt;

    public CancellationTokenSource TimeoutCancellation { get; } = timeoutCancellation;

    public Task? TimeoutTask { get; set; }
}
