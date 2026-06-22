namespace MasterServer.Services;

public enum GatewayClientSecretCredentialOperation : byte
{
    List = 1,
    Create = 2,
    Update = 3,
    RotateSecret = 4,
    Remove = 5
}
