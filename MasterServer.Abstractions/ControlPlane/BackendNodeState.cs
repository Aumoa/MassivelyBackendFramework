namespace MasterServer.ControlPlane;

public enum BackendNodeState : byte
{
    Open = 1,
    Full = 2,
    Draining = 3,
    Maintenance = 4,
    Unavailable = 5
}
