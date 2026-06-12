namespace GatewayServer.Options;

public sealed record BackendRouteOptions
{
    public string[] AllowedBackendKinds { get; set; } = [];

    public int RequestTimeoutMilliseconds { get; set; } = 30000;
}
