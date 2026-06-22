using MasterServer.ControlPlane;

namespace BackendServer.Services;

internal interface IDirectConnectCodeValidator
{
    Task<DirectConnectCodeValidationResponse> ValidateDirectConnectCodeAsync(
        string code,
        string gatewayNodeId,
        string gatewayMasterConnectionId,
        CancellationToken cancellationToken);
}
