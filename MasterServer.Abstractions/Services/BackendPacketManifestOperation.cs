namespace MasterServer.Services;

public enum BackendPacketManifestOperation : byte
{
    List = 0,
    Create = 1,
    Update = 2,
    Deprecate = 3,
    Remove = 4
}
