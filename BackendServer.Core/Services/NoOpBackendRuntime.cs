using BackendServer.Runtime;
using Microsoft.Extensions.Logging;

namespace BackendServer.Services;

internal sealed class NoOpBackendRuntime(ILogger<NoOpBackendRuntime> logger) : IBackendRuntime
{
    public ValueTask StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("No Backend runtime is configured. Gateway packets will be accepted and ignored.");
        return ValueTask.CompletedTask;
    }

    public ValueTask StopAsync(CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }

    public ValueTask HandleGatewayChannelOpenedAsync(
        BackendGatewayChannelOpenContext context,
        CancellationToken cancellationToken)
    {
        logger.LogDebug(
            "Ignored Gateway channel open because no Backend runtime is configured. GatewayNodeId={GatewayNodeId}, ChannelId={ChannelId}, PrincipalSubjectId={PrincipalSubjectId}.",
            context.GatewayNodeId,
            context.ChannelId,
            context.PrincipalSubjectId);
        return ValueTask.CompletedTask;
    }

    public ValueTask HandleGatewayPacketAsync(
        BackendGatewayPacketContext context,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        logger.LogDebug(
            "Ignored Gateway packet because no Backend runtime is configured. GatewayNodeId={GatewayNodeId}, ChannelId={ChannelId}, PacketId={PacketId}, PayloadLength={PayloadLength}.",
            context.GatewayNodeId,
            context.ChannelId,
            context.PacketId,
            payload.Length);
        return ValueTask.CompletedTask;
    }

    public ValueTask HandleGatewayChannelClosedAsync(
        BackendGatewayChannelCloseContext context,
        CancellationToken cancellationToken)
    {
        logger.LogDebug(
            "Ignored Gateway channel close because no Backend runtime is configured. GatewayNodeId={GatewayNodeId}, ChannelId={ChannelId}, Reason={Reason}.",
            context.GatewayNodeId,
            context.ChannelId,
            context.Reason);
        return ValueTask.CompletedTask;
    }
}
