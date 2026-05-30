using DedicatedServer.Runtime;
using Microsoft.Extensions.Logging;

namespace DedicatedServer.Services;

internal sealed class NoOpDedicatedWorldRuntime(ILogger<NoOpDedicatedWorldRuntime> logger) : IDedicatedWorldRuntime
{
    public ValueTask StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("No dedicated world runtime is configured. Gateway packets will be accepted and ignored.");
        return ValueTask.CompletedTask;
    }

    public ValueTask StopAsync(CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }

    public ValueTask HandleGatewayPacketAsync(
        DedicatedGatewayPacketContext context,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        logger.LogDebug(
            "Ignored Gateway packet because no world runtime is configured. GatewayNodeId={GatewayNodeId}, PacketId={PacketId}, PayloadLength={PayloadLength}.",
            context.GatewayNodeId,
            context.PacketId,
            payload.Length);
        return ValueTask.CompletedTask;
    }
}
