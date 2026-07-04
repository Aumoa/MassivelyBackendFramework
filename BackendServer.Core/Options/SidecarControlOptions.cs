namespace BackendServer.Options;

public sealed record SidecarControlOptions
{
    public bool Enabled { get; set; }

    public string IPAddress { get; set; } = "127.0.0.1";

    public int Port { get; set; } = 11702;

    public int Backlog { get; set; } = 64;

    public int RequestTimeoutMilliseconds { get; set; } = 5000;
}
