namespace GatewayServer.Options;

public record ConnectionManagerOptions
{
    public string IPAddress { get; set; } = "::1";
    public int Port { get; set; } = 11501;
    public bool UseTls { get; set; } = true;
}
