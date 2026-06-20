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

    public int RouteOpenRateLimitWindowMilliseconds { get; set; } = 1000;

    public int MaxRouteOpenAttemptsPerClientPerWindow { get; set; } = 32;

    public int ExchangeTimeoutMilliseconds { get; set; } = 30000;

    public int MaxPendingExchangesPerRoute { get; set; } = 1024;

    public int MaxPendingExchangesPerRoutePerDirection { get; set; } = 512;

    public int ExchangeRateLimitWindowMilliseconds { get; set; } = 1000;

    public int MaxClientOriginExchangeCreatesPerRoutePerWindow { get; set; } = 256;

    public int MaxBackendOriginExchangeCreatesPerRoutePerWindow { get; set; } = 256;
}
