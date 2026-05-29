namespace MasterServer.ControlPlane;

public sealed class MasterSocketEndpoint
{
    public MasterSocketEndpoint(string ipAddress, int port, bool useTls)
    {
        IPAddress = ipAddress;
        Port = port;
        UseTls = useTls;
    }

    public string IPAddress { get; }

    public int Port { get; }

    public bool UseTls { get; }
}
