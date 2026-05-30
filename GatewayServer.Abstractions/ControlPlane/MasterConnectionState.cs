namespace GatewayServer.ControlPlane;

public enum MasterConnectionState
{
    Disabled = 0,
    Disconnected = 1,
    Connecting = 2,
    Handshaking = 3,
    Trusted = 4,
    Reconnecting = 5,
    Stopping = 6
}
