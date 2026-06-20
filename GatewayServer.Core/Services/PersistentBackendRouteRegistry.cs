using System.Collections.Concurrent;
using GatewayServer.Options;
using GatewayServer.Protocols;
using MasterServer.ControlPlane;
using Microsoft.Extensions.Logging;

namespace GatewayServer.Services;

internal sealed class PersistentBackendRouteRegistry<TOwner>(
    BackendRouteOptions options,
    IGatewayBackendRouteTokenGenerator tokenGenerator,
    ILogger logger)
    where TOwner : class
{
    private readonly object m_RegistrationSync = new();
    private readonly ConcurrentDictionary<string, PersistentBackendRoute<TOwner>> m_Routes = new(StringComparer.Ordinal);

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

        throw new UnauthorizedAccessException($"Backend kind '{normalizedBackendKind}' is not enabled for persistent client routing.");
    }

    public PersistentBackendRoute<TOwner> Open(
        string backendKind,
        TOwner owner,
        CancellationToken cancellationToken)
    {
        if (owner == null)
        {
            throw new ArgumentNullException(nameof(owner));
        }

        var normalizedBackendKind = RequireAllowedBackendKind(backendKind);
        PersistentBackendRoute<TOwner>? route = null;

        lock (m_RegistrationSync)
        {
            EnsureRouteCapacity(owner);

            for (var attempt = 0; attempt < 8; attempt++)
            {
                var now = DateTimeOffset.UtcNow;
                route = new PersistentBackendRoute<TOwner>(
                    tokenGenerator.Generate(),
                    normalizedBackendKind,
                    owner,
                    now,
                    now.AddMilliseconds(GetRouteLifetimeMilliseconds()),
                    CancellationTokenSource.CreateLinkedTokenSource(cancellationToken));

                if (m_Routes.TryAdd(route.RouteToken.Value, route))
                {
                    route.TimeoutTask = ExpireRouteAsync(route);
                    return route;
                }

                route.Close("duplicate route token");
                route.TimeoutCancellation.Dispose();
                route = null;
            }
        }

        throw new InvalidOperationException("Gateway could not allocate a unique Backend route token.");
    }

    public bool TryGet(
        GatewayBackendRouteToken routeToken,
        out PersistentBackendRoute<TOwner> route)
    {
        if (routeToken == null)
        {
            throw new ArgumentNullException(nameof(routeToken));
        }

        return m_Routes.TryGetValue(routeToken.Value, out route!);
    }

    public bool Close(
        GatewayBackendRouteToken routeToken,
        string reason)
    {
        if (routeToken == null)
        {
            throw new ArgumentNullException(nameof(routeToken));
        }

        if (reason == null)
        {
            throw new ArgumentNullException(nameof(reason));
        }

        if (!m_Routes.TryRemove(routeToken.Value, out var route))
        {
            return false;
        }

        route.Close(reason);
        return true;
    }

    public int RemoveOwnerRoutes(
        TOwner owner,
        string reason)
    {
        if (owner == null)
        {
            throw new ArgumentNullException(nameof(owner));
        }

        if (reason == null)
        {
            throw new ArgumentNullException(nameof(reason));
        }

        var removed = 0;
        foreach (var pair in m_Routes.ToArray())
        {
            if (ReferenceEquals(pair.Value.Owner, owner) &&
                Close(pair.Value.RouteToken, reason))
            {
                removed++;
            }
        }

        return removed;
    }

    public void CancelAll()
    {
        foreach (var route in m_Routes.Values.ToArray())
        {
            Close(route.RouteToken, "Gateway route registry stopped.");
        }
    }

    public ServiceAdminStatusItem[] GetStatusItems()
    {
        var routes = m_Routes.Values.ToArray();
        var allowedBackendKinds = options.AllowedBackendKinds ?? [];
        var items = new List<ServiceAdminStatusItem>
        {
            new("Persistent Backend routes", "Allowed kinds", allowedBackendKinds.Length == 0
                ? "None"
                : string.Join(", ", allowedBackendKinds.OrderBy(static item => item, StringComparer.Ordinal))),
            new("Persistent Backend routes", "Open routes", routes.Length.ToString()),
            new("Persistent Backend routes", "Pending client exchanges", routes.Sum(static route => route.PendingClientExchangeCount).ToString()),
            new("Persistent Backend routes", "Pending backend exchanges", routes.Sum(static route => route.PendingBackendExchangeCount).ToString()),
            new("Persistent Backend routes", "Route lifetime", $"{GetRouteLifetimeMilliseconds()} ms"),
            new("Persistent Backend routes", "Max open routes", FormatLimit(options.MaxOpenRoutes)),
            new("Persistent Backend routes", "Max open routes per client", FormatLimit(options.MaxOpenRoutesPerClient))
        };

        foreach (var group in routes
                     .GroupBy(static route => route.BackendKind, StringComparer.Ordinal)
                     .OrderBy(static group => group.Key, StringComparer.Ordinal))
        {
            items.Add(new ServiceAdminStatusItem($"Persistent Backend route {group.Key}", "Open routes", group.Count().ToString()));
            items.Add(new ServiceAdminStatusItem(
                $"Persistent Backend route {group.Key}",
                "Pending client exchanges",
                group.Sum(static route => route.PendingClientExchangeCount).ToString()));
            items.Add(new ServiceAdminStatusItem(
                $"Persistent Backend route {group.Key}",
                "Pending backend exchanges",
                group.Sum(static route => route.PendingBackendExchangeCount).ToString()));
        }

        return [.. items];
    }

    private void EnsureRouteCapacity(TOwner owner)
    {
        var maxOpenRoutes = options.MaxOpenRoutes;
        if (maxOpenRoutes > 0 &&
            m_Routes.Count >= maxOpenRoutes)
        {
            throw new InvalidOperationException("Gateway persistent Backend route capacity is exhausted.");
        }

        var maxOpenRoutesPerClient = options.MaxOpenRoutesPerClient;
        if (maxOpenRoutesPerClient > 0 &&
            m_Routes.Values.Count(route => ReferenceEquals(route.Owner, owner)) >= maxOpenRoutesPerClient)
        {
            throw new InvalidOperationException("Gateway persistent Backend route capacity is exhausted for this client.");
        }
    }

    private async Task ExpireRouteAsync(PersistentBackendRoute<TOwner> route)
    {
        try
        {
            var delay = route.ExpiresAt - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, route.TimeoutCancellation.Token).ConfigureAwait(false);
            }

            if (m_Routes.TryRemove(route.RouteToken.Value, out _))
            {
                route.Close("expired");
                logger.LogWarning(
                    "Gateway persistent Backend route expired. BackendKind={BackendKind}, RouteTokenFingerprint={RouteTokenFingerprint}, CreatedAt={CreatedAt:O}, ExpiresAt={ExpiresAt:O}.",
                    route.BackendKind,
                    tokenGenerator.GetFingerprint(route.RouteToken),
                    route.CreatedAt,
                    route.ExpiresAt);
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
                "Gateway persistent Backend route expiration task failed. BackendKind={BackendKind}, RouteTokenFingerprint={RouteTokenFingerprint}.",
                route.BackendKind,
                tokenGenerator.GetFingerprint(route.RouteToken));
        }
        finally
        {
            route.TimeoutCancellation.Dispose();
        }
    }

    private int GetRouteLifetimeMilliseconds()
    {
        return Math.Max(1, options.RouteLifetimeMilliseconds);
    }

    private static string FormatLimit(int limit)
    {
        return limit <= 0 ? "Unlimited" : limit.ToString();
    }
}

internal enum PersistentBackendRouteState
{
    Open,
    Closed
}

internal sealed class PersistentBackendRoute<TOwner>(
    GatewayBackendRouteToken routeToken,
    string backendKind,
    TOwner owner,
    DateTimeOffset createdAt,
    DateTimeOffset expiresAt,
    CancellationTokenSource timeoutCancellation)
    where TOwner : class
{
    private int m_Closed;
    private readonly ConcurrentDictionary<GatewayBackendExchangeId, PersistentBackendRouteExchange> m_ClientOriginExchanges = [];
    private readonly ConcurrentDictionary<GatewayBackendExchangeId, PersistentBackendRouteExchange> m_BackendOriginExchanges = [];

    public GatewayBackendRouteToken RouteToken { get; } = routeToken;

    public string BackendKind { get; } = backendKind;

    public TOwner Owner { get; } = owner;

    public DateTimeOffset CreatedAt { get; } = createdAt;

    public DateTimeOffset ExpiresAt { get; } = expiresAt;

    public CancellationTokenSource TimeoutCancellation { get; } = timeoutCancellation;

    public Task? TimeoutTask { get; set; }

    public string CloseReason { get; private set; } = string.Empty;

    public int PendingClientExchangeCount => m_ClientOriginExchanges.Count;

    public int PendingBackendExchangeCount => m_BackendOriginExchanges.Count;

    public PersistentBackendRouteState State => Volatile.Read(ref m_Closed) == 0
        ? PersistentBackendRouteState.Open
        : PersistentBackendRouteState.Closed;

    public PersistentBackendRouteExchange RegisterClientOriginExchange(
        GatewayBackendExchangeId exchangeId,
        ushort requestPacketId,
        ushort requestVersion)
    {
        if (State != PersistentBackendRouteState.Open)
        {
            throw new InvalidOperationException("Persistent Backend route is not open.");
        }

        var exchange = new PersistentBackendRouteExchange(
            exchangeId,
            GatewayBackendRouteDirection.ClientToBackend,
            requestPacketId,
            requestVersion,
            DateTimeOffset.UtcNow);
        if (!m_ClientOriginExchanges.TryAdd(exchangeId, exchange))
        {
            throw new InvalidOperationException("Client-origin Backend route exchange id is already active.");
        }

        if (State != PersistentBackendRouteState.Open)
        {
            m_ClientOriginExchanges.TryRemove(exchangeId, out _);
            throw new InvalidOperationException("Persistent Backend route is not open.");
        }

        return exchange;
    }

    public bool RemoveClientOriginExchange(GatewayBackendExchangeId exchangeId)
    {
        return m_ClientOriginExchanges.TryRemove(exchangeId, out _);
    }

    public bool ContainsClientOriginExchange(GatewayBackendExchangeId exchangeId)
    {
        return m_ClientOriginExchanges.ContainsKey(exchangeId);
    }

    public PersistentBackendRouteExchange RegisterBackendOriginExchange(
        GatewayBackendExchangeId exchangeId,
        ushort requestPacketId,
        ushort requestVersion)
    {
        if (State != PersistentBackendRouteState.Open)
        {
            throw new InvalidOperationException("Persistent Backend route is not open.");
        }

        var exchange = new PersistentBackendRouteExchange(
            exchangeId,
            GatewayBackendRouteDirection.BackendToClient,
            requestPacketId,
            requestVersion,
            DateTimeOffset.UtcNow);
        if (!m_BackendOriginExchanges.TryAdd(exchangeId, exchange))
        {
            throw new InvalidOperationException("Backend-origin Backend route exchange id is already active.");
        }

        if (State != PersistentBackendRouteState.Open)
        {
            m_BackendOriginExchanges.TryRemove(exchangeId, out _);
            throw new InvalidOperationException("Persistent Backend route is not open.");
        }

        return exchange;
    }

    public bool RemoveBackendOriginExchange(GatewayBackendExchangeId exchangeId)
    {
        return m_BackendOriginExchanges.TryRemove(exchangeId, out _);
    }

    public bool ContainsBackendOriginExchange(GatewayBackendExchangeId exchangeId)
    {
        return m_BackendOriginExchanges.ContainsKey(exchangeId);
    }

    public bool Close(string reason)
    {
        if (reason == null)
        {
            throw new ArgumentNullException(nameof(reason));
        }

        if (Interlocked.CompareExchange(ref m_Closed, 1, 0) != 0)
        {
            return false;
        }

        CloseReason = reason;
        m_ClientOriginExchanges.Clear();
        m_BackendOriginExchanges.Clear();
        TimeoutCancellation.Cancel();
        return true;
    }
}

internal sealed record PersistentBackendRouteExchange(
    GatewayBackendExchangeId ExchangeId,
    GatewayBackendRouteDirection Direction,
    ushort RequestPacketId,
    ushort RequestVersion,
    DateTimeOffset CreatedAt);
