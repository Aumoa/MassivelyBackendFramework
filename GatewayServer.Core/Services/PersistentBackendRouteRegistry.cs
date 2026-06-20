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
    private readonly ConcurrentDictionary<TOwner, FixedWindowRateCounter> m_OpenAttemptCounters = new();
    private readonly ConcurrentDictionary<string, FixedWindowRateCounter> m_PrincipalOpenAttemptCounters = new(StringComparer.Ordinal);
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

    public void RequireOpenAttemptAllowed(TOwner owner)
    {
        RequireOpenAttemptAllowed(owner, principalSubjectId: null);
    }

    public void RequireOpenAttemptAllowed(
        TOwner owner,
        string? principalSubjectId)
    {
        if (owner == null)
        {
            throw new ArgumentNullException(nameof(owner));
        }

        var limit = options.MaxRouteOpenAttemptsPerClientPerWindow;
        if (limit > 0)
        {
            var counter = m_OpenAttemptCounters.GetOrAdd(
                owner,
                static _ => new FixedWindowRateCounter());
            counter.IncrementOrThrow(
                limit,
                options.RouteOpenRateLimitWindowMilliseconds,
                DateTimeOffset.UtcNow,
                "Gateway persistent Backend route open rate limit was exceeded.");
        }

        var normalizedPrincipalSubjectId = NormalizePrincipalSubjectId(principalSubjectId);
        var principalLimit = options.MaxRouteOpenAttemptsPerPrincipalPerWindow;
        if (normalizedPrincipalSubjectId == null ||
            principalLimit <= 0)
        {
            return;
        }

        var principalCounter = m_PrincipalOpenAttemptCounters.GetOrAdd(
            normalizedPrincipalSubjectId,
            static _ => new FixedWindowRateCounter());
        principalCounter.IncrementOrThrow(
            principalLimit,
            options.RouteOpenRateLimitWindowMilliseconds,
            DateTimeOffset.UtcNow,
            "Gateway persistent Backend route principal open rate limit was exceeded.");
    }

    public PersistentBackendRoute<TOwner> Open(
        BackendRouteBinding backendBinding,
        TOwner owner,
        CancellationToken cancellationToken)
    {
        return Open(
            backendBinding,
            owner,
            principalSubjectId: null,
            cancellationToken);
    }

    public PersistentBackendRoute<TOwner> Open(
        BackendRouteBinding backendBinding,
        TOwner owner,
        string? principalSubjectId,
        CancellationToken cancellationToken)
    {
        if (backendBinding == null)
        {
            throw new ArgumentNullException(nameof(backendBinding));
        }

        if (owner == null)
        {
            throw new ArgumentNullException(nameof(owner));
        }

        var normalizedBackendKind = RequireAllowedBackendKind(backendBinding.BackendKind);
        var normalizedPrincipalSubjectId = NormalizePrincipalSubjectId(principalSubjectId);
        var normalizedBackendBinding = string.Equals(
            normalizedBackendKind,
            backendBinding.BackendKind,
            StringComparison.Ordinal)
            ? backendBinding
            : new BackendRouteBinding(
                normalizedBackendKind,
                backendBinding.NodeId,
                backendBinding.MasterConnectionId,
                backendBinding.DirectConnectionId);
        PersistentBackendRoute<TOwner>? route = null;

        lock (m_RegistrationSync)
        {
            EnsureRouteCapacity(owner, normalizedPrincipalSubjectId);

            for (var attempt = 0; attempt < 8; attempt++)
            {
                var now = DateTimeOffset.UtcNow;
                route = new PersistentBackendRoute<TOwner>(
                    tokenGenerator.Generate(),
                    normalizedBackendBinding,
                    owner,
                    normalizedPrincipalSubjectId,
                    now,
                    now.AddMilliseconds(GetRouteLifetimeMilliseconds()),
                    GetExchangeTimeoutMilliseconds(),
                    options.MaxPendingExchangesPerRoute,
                    options.MaxPendingExchangesPerRoutePerDirection,
                    GetExchangeRateLimitWindowMilliseconds(),
                    options.MaxClientOriginExchangeCreatesPerRoutePerWindow,
                    options.MaxBackendOriginExchangeCreatesPerRoutePerWindow,
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

    public PersistentBackendRoute<TOwner>[] RemoveBackendBindingRoutes(
        BackendRouteBinding backendBinding,
        string reason)
    {
        if (backendBinding == null)
        {
            throw new ArgumentNullException(nameof(backendBinding));
        }

        if (reason == null)
        {
            throw new ArgumentNullException(nameof(reason));
        }

        var removed = new List<PersistentBackendRoute<TOwner>>();
        foreach (var pair in m_Routes.ToArray())
        {
            if (pair.Value.BackendBinding == backendBinding &&
                m_Routes.TryRemove(pair.Key, out var route))
            {
                route.Close(reason);
                removed.Add(route);
            }
        }

        return [.. removed];
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

        m_OpenAttemptCounters.TryRemove(owner, out _);
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
            new("Persistent Backend routes", "Exchange timeout", $"{GetExchangeTimeoutMilliseconds()} ms"),
            new("Persistent Backend routes", "Max open routes", FormatLimit(options.MaxOpenRoutes)),
            new("Persistent Backend routes", "Max open routes per client", FormatLimit(options.MaxOpenRoutesPerClient)),
            new("Persistent Backend routes", "Max open routes per principal", FormatLimit(options.MaxOpenRoutesPerPrincipal)),
            new("Persistent Backend routes", "Route-open rate limit", FormatRateLimit(options.MaxRouteOpenAttemptsPerClientPerWindow, GetRouteOpenRateLimitWindowMilliseconds())),
            new("Persistent Backend routes", "Principal route-open rate limit", FormatRateLimit(options.MaxRouteOpenAttemptsPerPrincipalPerWindow, GetRouteOpenRateLimitWindowMilliseconds())),
            new("Persistent Backend routes", "Max pending exchanges per route", FormatLimit(options.MaxPendingExchangesPerRoute)),
            new("Persistent Backend routes", "Max pending exchanges per route direction", FormatLimit(options.MaxPendingExchangesPerRoutePerDirection)),
            new("Persistent Backend routes", "Client-origin exchange rate limit", FormatRateLimit(options.MaxClientOriginExchangeCreatesPerRoutePerWindow, GetExchangeRateLimitWindowMilliseconds())),
            new("Persistent Backend routes", "Backend-origin exchange rate limit", FormatRateLimit(options.MaxBackendOriginExchangeCreatesPerRoutePerWindow, GetExchangeRateLimitWindowMilliseconds())),
            new("Persistent Backend routes", "Principal client-origin exchange rate limit", FormatRateLimit(options.MaxClientOriginExchangeCreatesPerPrincipalPerWindow, GetExchangeRateLimitWindowMilliseconds())),
            new("Persistent Backend routes", "Principal Backend-origin exchange rate limit", FormatRateLimit(options.MaxBackendOriginExchangeCreatesPerPrincipalPerWindow, GetExchangeRateLimitWindowMilliseconds()))
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

    private void EnsureRouteCapacity(
        TOwner owner,
        string? principalSubjectId)
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

        var maxOpenRoutesPerPrincipal = options.MaxOpenRoutesPerPrincipal;
        if (!string.IsNullOrEmpty(principalSubjectId) &&
            maxOpenRoutesPerPrincipal > 0 &&
            m_Routes.Values.Count(route => string.Equals(
                route.PrincipalSubjectId,
                principalSubjectId,
                StringComparison.Ordinal)) >= maxOpenRoutesPerPrincipal)
        {
            throw new InvalidOperationException("Gateway persistent Backend route capacity is exhausted for this principal.");
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

    private int GetExchangeTimeoutMilliseconds()
    {
        return Math.Max(1, options.ExchangeTimeoutMilliseconds);
    }

    private int GetRouteOpenRateLimitWindowMilliseconds()
    {
        return Math.Max(1, options.RouteOpenRateLimitWindowMilliseconds);
    }

    private int GetExchangeRateLimitWindowMilliseconds()
    {
        return Math.Max(1, options.ExchangeRateLimitWindowMilliseconds);
    }

    private static string FormatLimit(int limit)
    {
        return limit <= 0 ? "Unlimited" : limit.ToString();
    }

    private static string FormatRateLimit(
        int limit,
        int windowMilliseconds)
    {
        return limit <= 0
            ? "Unlimited"
            : $"{limit} per {windowMilliseconds} ms";
    }

    private static string? NormalizePrincipalSubjectId(string? principalSubjectId)
    {
        return string.IsNullOrWhiteSpace(principalSubjectId)
            ? null
            : principalSubjectId.Trim();
    }
}

internal enum PersistentBackendRouteState
{
    Open,
    Closed
}

internal sealed class FixedWindowRateCounter
{
    private readonly object m_Sync = new();
    private DateTimeOffset m_WindowStartedAt;
    private int m_Count;

    public void IncrementOrThrow(
        int limit,
        int windowMilliseconds,
        DateTimeOffset now,
        string message)
    {
        if (limit <= 0)
        {
            return;
        }

        var window = TimeSpan.FromMilliseconds(Math.Max(1, windowMilliseconds));
        lock (m_Sync)
        {
            if (m_WindowStartedAt == default ||
                now - m_WindowStartedAt >= window)
            {
                m_WindowStartedAt = now;
                m_Count = 0;
            }

            if (m_Count >= limit)
            {
                throw new InvalidOperationException(message);
            }

            m_Count++;
        }
    }
}

internal sealed class PersistentBackendRoute<TOwner>(
    GatewayBackendRouteToken routeToken,
    BackendRouteBinding backendBinding,
    TOwner owner,
    string? principalSubjectId,
    DateTimeOffset createdAt,
    DateTimeOffset expiresAt,
    int exchangeTimeoutMilliseconds,
    int maxPendingExchanges,
    int maxPendingExchangesPerDirection,
    int exchangeRateLimitWindowMilliseconds,
    int maxClientOriginExchangeCreatesPerWindow,
    int maxBackendOriginExchangeCreatesPerWindow,
    CancellationTokenSource timeoutCancellation)
    where TOwner : class
{
    private int m_Closed;
    private readonly FixedWindowRateCounter m_ClientOriginExchangeRateCounter = new();
    private readonly FixedWindowRateCounter m_BackendOriginExchangeRateCounter = new();
    private readonly ConcurrentDictionary<GatewayBackendExchangeId, PersistentBackendRouteExchange> m_ClientOriginExchanges = [];
    private readonly ConcurrentDictionary<GatewayBackendExchangeId, PersistentBackendRouteExchange> m_BackendOriginExchanges = [];

    public GatewayBackendRouteToken RouteToken { get; } = routeToken;

    public BackendRouteBinding BackendBinding { get; } = backendBinding;

    public string BackendKind => BackendBinding.BackendKind;

    public TOwner Owner { get; } = owner;

    public string? PrincipalSubjectId { get; } = principalSubjectId;

    public DateTimeOffset CreatedAt { get; } = createdAt;

    public DateTimeOffset ExpiresAt { get; } = expiresAt;

    public CancellationTokenSource TimeoutCancellation { get; } = timeoutCancellation;

    public Task? TimeoutTask { get; set; }

    public string CloseReason { get; private set; } = string.Empty;

    public int PendingClientExchangeCount
    {
        get
        {
            PruneExpiredExchanges(DateTimeOffset.UtcNow);
            return m_ClientOriginExchanges.Count;
        }
    }

    public int PendingBackendExchangeCount
    {
        get
        {
            PruneExpiredExchanges(DateTimeOffset.UtcNow);
            return m_BackendOriginExchanges.Count;
        }
    }

    public PersistentBackendRouteState State => Volatile.Read(ref m_Closed) == 0
        ? PersistentBackendRouteState.Open
        : PersistentBackendRouteState.Closed;

    public PersistentBackendRouteExchange RegisterClientOriginExchange(
        GatewayBackendExchangeId exchangeId,
        ushort requestPacketId,
        ushort requestVersion)
    {
        var now = DateTimeOffset.UtcNow;
        PruneExpiredExchanges(now);
        if (State != PersistentBackendRouteState.Open)
        {
            throw new InvalidOperationException("Persistent Backend route is not open.");
        }

        EnsureExchangeCapacity(m_ClientOriginExchanges, "client-origin");
        m_ClientOriginExchangeRateCounter.IncrementOrThrow(
            maxClientOriginExchangeCreatesPerWindow,
            exchangeRateLimitWindowMilliseconds,
            now,
            "Persistent Backend route client-origin exchange creation rate limit was exceeded.");
        var exchange = new PersistentBackendRouteExchange(
            exchangeId,
            GatewayBackendRouteDirection.ClientToBackend,
            requestPacketId,
            requestVersion,
            now,
            now.AddMilliseconds(exchangeTimeoutMilliseconds));
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
        return ContainsActiveExchange(m_ClientOriginExchanges, exchangeId, DateTimeOffset.UtcNow);
    }

    public PersistentBackendRouteExchange RegisterBackendOriginExchange(
        GatewayBackendExchangeId exchangeId,
        ushort requestPacketId,
        ushort requestVersion)
    {
        var now = DateTimeOffset.UtcNow;
        PruneExpiredExchanges(now);
        if (State != PersistentBackendRouteState.Open)
        {
            throw new InvalidOperationException("Persistent Backend route is not open.");
        }

        EnsureExchangeCapacity(m_BackendOriginExchanges, "backend-origin");
        m_BackendOriginExchangeRateCounter.IncrementOrThrow(
            maxBackendOriginExchangeCreatesPerWindow,
            exchangeRateLimitWindowMilliseconds,
            now,
            "Persistent Backend route Backend-origin exchange creation rate limit was exceeded.");
        var exchange = new PersistentBackendRouteExchange(
            exchangeId,
            GatewayBackendRouteDirection.BackendToClient,
            requestPacketId,
            requestVersion,
            now,
            now.AddMilliseconds(exchangeTimeoutMilliseconds));
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
        return ContainsActiveExchange(m_BackendOriginExchanges, exchangeId, DateTimeOffset.UtcNow);
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

    private void EnsureExchangeCapacity(
        ConcurrentDictionary<GatewayBackendExchangeId, PersistentBackendRouteExchange> directionExchanges,
        string directionName)
    {
        if (maxPendingExchanges > 0 &&
            m_ClientOriginExchanges.Count + m_BackendOriginExchanges.Count >= maxPendingExchanges)
        {
            throw new InvalidOperationException("Persistent Backend route pending exchange capacity is exhausted.");
        }

        if (maxPendingExchangesPerDirection > 0 &&
            directionExchanges.Count >= maxPendingExchangesPerDirection)
        {
            throw new InvalidOperationException($"Persistent Backend route {directionName} pending exchange capacity is exhausted.");
        }
    }

    private static bool ContainsActiveExchange(
        ConcurrentDictionary<GatewayBackendExchangeId, PersistentBackendRouteExchange> exchanges,
        GatewayBackendExchangeId exchangeId,
        DateTimeOffset now)
    {
        if (!exchanges.TryGetValue(exchangeId, out var exchange))
        {
            return false;
        }

        if (exchange.ExpiresAt > now)
        {
            return true;
        }

        exchanges.TryRemove(exchangeId, out _);
        return false;
    }

    private void PruneExpiredExchanges(DateTimeOffset now)
    {
        PruneExpiredExchanges(m_ClientOriginExchanges, now);
        PruneExpiredExchanges(m_BackendOriginExchanges, now);
    }

    private static void PruneExpiredExchanges(
        ConcurrentDictionary<GatewayBackendExchangeId, PersistentBackendRouteExchange> exchanges,
        DateTimeOffset now)
    {
        foreach (var pair in exchanges.ToArray())
        {
            if (pair.Value.ExpiresAt <= now)
            {
                exchanges.TryRemove(pair.Key, out _);
            }
        }
    }
}

internal sealed record PersistentBackendRouteExchange(
    GatewayBackendExchangeId ExchangeId,
    GatewayBackendRouteDirection Direction,
    ushort RequestPacketId,
    ushort RequestVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);
