using System;
using System.Threading;
using System.Threading.Tasks;

namespace DedicatedServer.Runtime;

public interface IDedicatedWorldRuntime
{
    ValueTask StartAsync(CancellationToken cancellationToken);

    ValueTask StopAsync(CancellationToken cancellationToken);

    ValueTask HandleGatewayPacketAsync(
        DedicatedGatewayPacketContext context,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken);
}
