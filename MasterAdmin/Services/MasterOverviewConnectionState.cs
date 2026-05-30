namespace MasterAdmin.Services;

public enum MasterOverviewConnectionState
{
    Disabled = 0,
    Disconnected = 1,
    Connecting = 2,
    Handshaking = 3,
    Connected = 4,
    Reconnecting = 5,
    Stopping = 6
}
