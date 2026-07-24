namespace BackendServer.Options;

using MasterServer.ControlPlane;

public sealed record MasterConnectionOptions
{
    public bool Enabled { get; set; } = true;

    public string IPAddress { get; set; } = "::1";

    public int Port { get; set; } = 11601;

    public bool UseTls { get; set; }

    public string ServerName { get; set; } = "localhost";

    public string NodeId { get; set; } = "backend-local";

    public string DisplayName { get; set; } = "Backend";

    public string BackendKind { get; set; } = "backend";

    public string BackendPacketManifestId { get; set; } = string.Empty;

    public string BackendPacketManifestHash { get; set; } = string.Empty;

    public BackendNodeState ServerState { get; set; } = BackendNodeState.Open;

    public string ServerDescriptorVersion { get; set; } = "1";

    public string ServerDescriptorJson { get; set; } = "{}";

    public GatewayAuthenticationMethodOptions[] GatewayAuthenticationMethods { get; set; } = [];

    public string SharedSecret { get; set; } = string.Empty;

    public int ReconnectDelayMilliseconds { get; set; } = 5000;

    public int HandshakeTimeoutMilliseconds { get; set; } = 5000;
}
