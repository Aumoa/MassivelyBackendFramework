using MasterServer.ControlPlane;

namespace MasterServer.Services;

internal interface IDirectConnectCodeStore
{
    ValueTask<DirectConnectCodeTicket> CreateAsync(
        string gatewayMasterConnectionId,
        string gatewayNodeId,
        MasterNodeKind targetNodeKind,
        string targetMasterConnectionId,
        string targetNodeId,
        CancellationToken cancellationToken);

    ValueTask<DirectConnectCodeTicket?> ConsumeAsync(
        string code,
        string expectedGatewayMasterConnectionId,
        string expectedGatewayNodeId,
        MasterNodeKind expectedTargetNodeKind,
        string expectedTargetMasterConnectionId,
        string expectedTargetNodeId,
        CancellationToken cancellationToken);
}

internal sealed record DirectConnectCodeTicket(
    string Code,
    string GatewayMasterConnectionId,
    string GatewayNodeId,
    MasterNodeKind TargetNodeKind,
    string TargetMasterConnectionId,
    string TargetNodeId,
    DateTimeOffset ExpiresAt);
