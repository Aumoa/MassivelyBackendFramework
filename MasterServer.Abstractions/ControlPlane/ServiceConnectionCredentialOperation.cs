namespace MasterServer.ControlPlane;

public enum ServiceConnectionCredentialOperation : byte
{
    List = 1,
    Create = 2,
    Update = 3,
    RotateSecret = 4,
    Remove = 5
}
