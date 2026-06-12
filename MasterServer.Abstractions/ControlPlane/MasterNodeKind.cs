namespace MasterServer.ControlPlane;

public enum MasterNodeKind
{
    Unknown = 0,
    Gateway = 1,
    Dedicated = 2,
    MasterAdmin = 3,
    Backend = 4
}
