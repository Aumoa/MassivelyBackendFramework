namespace GatewayServer.Options;

public sealed record BackendRouteOptions
{
    public string[] AllowedBackendKinds { get; set; } = [];
}
