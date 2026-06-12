using MasterServer.ControlPlane;

namespace UnityRemoteDebug.Backend.Services;

internal interface IDirectConnectCodeValidator
{
    Task<DirectConnectCodeValidationResponse> ValidateDirectConnectCodeAsync(
        string code,
        string gatewayNodeId,
        string gatewayMasterConnectionId,
        CancellationToken cancellationToken);
}
