namespace MasterServer.Options;

public sealed record MasterSocketOptions
{
    public string IPAddress { get; set; } = "::1";

    public int Port { get; set; } = 11601;

    public bool UseTls { get; set; }

    public string CertificateSubjectName { get; set; } = "localhost";

    public int Backlog { get; set; } = 512;

    public int ReceiveBufferSize { get; set; } = 4096;

    public string NodeAuthSecret { get; set; } = string.Empty;

    public int HandshakeTimeoutMilliseconds { get; set; } = 5000;
}
