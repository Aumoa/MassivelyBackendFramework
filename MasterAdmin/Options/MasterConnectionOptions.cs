namespace MasterAdmin.Options;

public sealed record MasterConnectionOptions
{
    public bool Enabled { get; set; } = true;

    public string IPAddress { get; set; } = "::1";

    public int Port { get; set; } = 11601;

    public bool UseTls { get; set; }

    public string ServerName { get; set; } = "localhost";

    public string NodeId { get; set; } = "master-admin-local";

    public string DisplayName { get; set; } = "Master Admin";

    public string SharedSecret { get; set; } = string.Empty;

    public int ReconnectDelayMilliseconds { get; set; } = 5000;

    public int HandshakeTimeoutMilliseconds { get; set; } = 5000;
}
