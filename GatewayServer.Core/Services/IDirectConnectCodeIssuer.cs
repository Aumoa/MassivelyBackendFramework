using MasterServer.ControlPlane;

namespace GatewayServer.Services;

internal interface IDirectConnectCodeIssuer
{
    Task<DirectConnectCodeResponse> RequestDirectConnectCodeAsync(
        MasterNodeKind targetNodeKind,
        string targetMasterConnectionId,
        CancellationToken cancellationToken);
}
