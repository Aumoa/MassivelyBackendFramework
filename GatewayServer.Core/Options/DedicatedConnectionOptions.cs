namespace GatewayServer.Options;

public sealed record DedicatedConnectionOptions
{
    public bool Enabled { get; set; } = true;

    public string ServerName { get; set; } = "localhost";

    public string SharedSecret { get; set; } = string.Empty;

    public int ReconnectDelayMilliseconds { get; set; } = 5000;

    public int HandshakeTimeoutMilliseconds { get; set; } = 5000;
}
