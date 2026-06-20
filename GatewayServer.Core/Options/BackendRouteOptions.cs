namespace GatewayServer.Options;

public sealed record BackendRouteOptions
{
    public string[] AllowedBackendKinds { get; set; } = [];

    public int RequestTimeoutMilliseconds { get; set; } = 30000;

    public int MaxPendingRoutes { get; set; } = 4096;

    public int MaxPendingRoutesPerClient { get; set; } = 64;

    public int RouteLifetimeMilliseconds { get; set; } = 300000;

    public int MaxOpenRoutes { get; set; } = 4096;

    public int MaxOpenRoutesPerClient { get; set; } = 64;

    public int ExchangeTimeoutMilliseconds { get; set; } = 30000;

    public int MaxPendingExchangesPerRoute { get; set; } = 1024;

    public int MaxPendingExchangesPerRoutePerDirection { get; set; } = 512;
}
