using System;
using System.Threading;
using System.Threading.Tasks;

namespace BackendServer.Runtime;

public interface IBackendRuntime
{
    ValueTask StartAsync(CancellationToken cancellationToken);

    ValueTask StopAsync(CancellationToken cancellationToken);

    ValueTask HandleGatewayPacketAsync(
        BackendGatewayPacketContext context,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken);

    ValueTask HandleGatewayChannelClosedAsync(
        BackendGatewayChannelCloseContext context,
        CancellationToken cancellationToken);
}
