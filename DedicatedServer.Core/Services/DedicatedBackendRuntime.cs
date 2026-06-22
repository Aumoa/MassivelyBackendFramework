using BackendServer.Runtime;
using DedicatedServer.Runtime;

namespace DedicatedServer.Services;

internal sealed class DedicatedBackendRuntime(IDedicatedWorldRuntime runtime) : IBackendRuntime
{
    public ValueTask StartAsync(CancellationToken cancellationToken)
    {
        return runtime.StartAsync(cancellationToken);
    }

    public ValueTask StopAsync(CancellationToken cancellationToken)
    {
        return runtime.StopAsync(cancellationToken);
    }

    public ValueTask HandleGatewayPacketAsync(
        BackendGatewayPacketContext context,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        var dedicatedContext = new DedicatedGatewayPacketContext(
            context.GatewayNodeId,
            context.GatewayConnectionId,
            context.ChannelId,
            context.Kind,
            context.PacketId,
            context.Version,
            context.ReceivedAt);
        return runtime.HandleGatewayPacketAsync(dedicatedContext, payload, cancellationToken);
    }

    public ValueTask HandleGatewayChannelClosedAsync(
        BackendGatewayChannelCloseContext context,
        CancellationToken cancellationToken)
    {
        var dedicatedContext = new DedicatedGatewayChannelCloseContext(
            context.GatewayNodeId,
            context.GatewayConnectionId,
            context.ChannelId,
            context.Reason,
            context.ReceivedAt);
        return runtime.HandleGatewayChannelClosedAsync(dedicatedContext, cancellationToken);
    }
}
