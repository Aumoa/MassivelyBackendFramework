namespace UnityRemoteDebug.Backend.Options;

public sealed record MasterConnectionOptions
{
    public bool Enabled { get; set; } = true;

    public string IPAddress { get; set; } = "::1";

    public int Port { get; set; } = 11601;

    public bool UseTls { get; set; }

    public string ServerName { get; set; } = "localhost";

    public string NodeId { get; set; } = "unity-remotedebug-backend-local";

    public string DisplayName { get; set; } = "Unity RemoteDebug Backend";

    public string SharedSecret { get; set; } = string.Empty;

    public int ReconnectDelayMilliseconds { get; set; } = 5000;

    public int HandshakeTimeoutMilliseconds { get; set; } = 5000;
}
