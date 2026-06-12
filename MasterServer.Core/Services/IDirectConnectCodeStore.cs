namespace MasterServer.Services;

internal interface IDirectConnectCodeStore
{
    ValueTask<DirectConnectCodeTicket> CreateAsync(
        string gatewayMasterConnectionId,
        string gatewayNodeId,
        string dedicatedMasterConnectionId,
        string dedicatedNodeId,
        CancellationToken cancellationToken);

    ValueTask<DirectConnectCodeTicket?> ConsumeAsync(
        string code,
        CancellationToken cancellationToken);
}

internal sealed record DirectConnectCodeTicket(
    string Code,
    string GatewayMasterConnectionId,
    string GatewayNodeId,
    string DedicatedMasterConnectionId,
    string DedicatedNodeId,
    DateTimeOffset ExpiresAt);
