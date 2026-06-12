using MasterServer.ControlPlane;

namespace GatewayServer.Services;

internal interface IDirectConnectCodeIssuer
{
    Task<DirectConnectCodeResponse> RequestDirectConnectCodeAsync(
        DedicatedNodeEndpoint dedicatedNode,
        CancellationToken cancellationToken);
}
