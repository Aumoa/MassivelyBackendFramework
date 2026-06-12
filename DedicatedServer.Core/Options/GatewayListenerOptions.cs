namespace DedicatedServer.Options;

public sealed record GatewayListenerOptions
{
    public string IPAddress { get; set; } = "::1";

    public int Port { get; set; } = 11701;

    public bool UseTls { get; set; }

    public string CertificateSubjectName { get; set; } = "localhost";

    public int Backlog { get; set; } = 512;

    public int HandshakeTimeoutMilliseconds { get; set; } = 5000;
}
