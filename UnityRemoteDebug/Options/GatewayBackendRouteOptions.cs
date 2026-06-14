namespace UnityRemoteDebug.Options;

public sealed class GatewayBackendRouteOptions
{
    public bool Enabled { get; set; } = true;

    public string IPAddress { get; set; } = "::1";

    public int Port { get; set; } = 11501;

    public bool UseTls { get; set; } = true;

    public string ServerName { get; set; } = "localhost";

    public string BackendKind { get; set; } = "UnityRemoteDebug";

    public int RequestTimeoutMilliseconds { get; set; } = 5000;
}
