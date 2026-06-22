using System;
using System.Threading;
using System.Threading.Tasks;

namespace BackendServer.Runtime;

public interface IBackendGatewayChannelSender
{
    ValueTask SendNotifyAsync(
        BackendGatewayChannel channel,
        ushort packetId,
        ushort version,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken);

    ValueTask SendRequestAsync(
        BackendGatewayChannel channel,
        ushort packetId,
        ushort version,
        Guid exchangeId,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken);

    ValueTask SendResponseAsync(
        BackendGatewayChannel channel,
        ushort packetId,
        ushort version,
        Guid exchangeId,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken);

    ValueTask CloseAsync(
        BackendGatewayChannel channel,
        string reason,
        CancellationToken cancellationToken);
}
