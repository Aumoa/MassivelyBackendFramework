namespace GatewayServer.Options;

public sealed record DedicatedConnectionOptions
{
    public bool Enabled { get; set; } = true;

    public string ServerName { get; set; } = "localhost";

    public int ReconnectDelayMilliseconds { get; set; } = 5000;

    public int HandshakeTimeoutMilliseconds { get; set; } = 5000;
}
