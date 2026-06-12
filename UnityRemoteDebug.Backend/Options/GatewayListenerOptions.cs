namespace UnityRemoteDebug.Backend.Options;

public sealed record GatewayListenerOptions
{
    public string IPAddress { get; set; } = "::1";

    public int Port { get; set; } = 11708;

    public bool UseTls { get; set; }
}
