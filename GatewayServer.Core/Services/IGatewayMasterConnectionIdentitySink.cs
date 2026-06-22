namespace GatewayServer.Services;

internal interface IGatewayMasterConnectionIdentitySink
{
    void SetMasterConnectionId(string? masterConnectionId);
}
