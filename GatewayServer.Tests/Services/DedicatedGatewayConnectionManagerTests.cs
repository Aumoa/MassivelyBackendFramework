using System.Net;
using System.Net.Sockets;
using DedicatedServer.Options;
using DedicatedServer.Runtime;
using DedicatedServer.Services;
using GatewayServer.Protocols;
using MasterServer.ControlPlane;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PacketCore;
using Xunit;

namespace GatewayServer.Tests.Services;

public sealed class DedicatedGatewayConnectionManagerTests
{
    [Fact]
    public async Task GatewayHandshake_AcceptsBackendTicketAndRoutesChannelData()
    {
        var port = GetFreeTcpPort();
        var runtime = new RecordingWorldRuntime();
        var validator = new RecordingDirectConnectCodeValidator(MasterNodeKind.Backend);
        var manager = CreateManager(port, runtime, validator);
        await manager.StartAsync(CancellationToken.None);

        using var client = new TcpClient();
        try
        {
            await client.ConnectAsync(IPAddress.Loopback, port);
            await using var stream = client.GetStream();
            await CompleteGatewayHandshakeAsync(stream);

            var validation = await validator.Validation.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("direct-code", validation.Code);
            Assert.Equal("gateway-test", validation.GatewayNodeId);
            Assert.Equal("gateway-master-a", validation.GatewayMasterConnectionId);

            byte[] payload = [7, 8, 9];
            var envelope = new GatewayBackendChannelDataEnvelope(
                channelId: 123,
                PacketKind.Notify,
                routedPacketId: 501,
                routedVersion: 2,
                exchangeId: null,
                payload);
            using (var frame = PacketCodec.Encode(
                       PacketKind.Notify,
                       Pid.GATE_BACKEND_CHANNEL_DATA,
                       GatewayBackendChannelDataEnvelope.ProtocolVersion,
                       envelope,
                       GatewayBackendChannelDataEnvelope.Codec))
            {
                await PacketFrameWriter.WriteAsync(stream, frame, CancellationToken.None);
            }

            var received = await runtime.Packet.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("gateway-test", received.Context.GatewayNodeId);
            Assert.Equal((uint)123, received.Context.ChannelId);
            Assert.Equal(PacketKind.Notify, received.Context.Kind);
            Assert.Equal((ushort)501, received.Context.PacketId);
            Assert.Equal((ushort)2, received.Context.Version);
            Assert.Equal(payload, received.Payload);
        }
        finally
        {
            await manager.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task GatewayHandshake_RejectsDedicatedTargetTicket()
    {
        var port = GetFreeTcpPort();
        var runtime = new RecordingWorldRuntime();
        var validator = new RecordingDirectConnectCodeValidator(MasterNodeKind.Dedicated);
        var manager = CreateManager(port, runtime, validator);
        await manager.StartAsync(CancellationToken.None);

        using var client = new TcpClient();
        try
        {
            await client.ConnectAsync(IPAddress.Loopback, port);
            await using var stream = client.GetStream();
            await ReadControlFrameAsync(stream, MasterControlPacketIds.NodeAuthChallenge, NodeAuthChallenge.Codec);
            await WriteControlFrameAsync(
                stream,
                MasterControlPacketIds.NodeHello,
                new NodeHello(
                    MasterNodeKind.Gateway,
                    "gateway-test",
                    "Gateway Test",
                    MasterControlProtocol.SchemaVersion,
                    "gateway-master-a"),
                NodeHello.Codec);
            await WriteControlFrameAsync(
                stream,
                MasterControlPacketIds.DirectConnectCode,
                new DirectConnectCode("direct-code"),
                DirectConnectCode.Codec);

            await validator.Validation.Task.WaitAsync(TimeSpan.FromSeconds(5));
            using var accepted = await TryReadFrameAsync(stream, TimeSpan.FromMilliseconds(500));

            Assert.True(
                accepted == null || accepted.Header.PacketId != MasterControlPacketIds.NodeAccepted,
                "Dedicated listener must not accept direct-connect tickets issued for the legacy Dedicated target kind.");
            Assert.False(runtime.Packet.Task.IsCompleted);
        }
        finally
        {
            await manager.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task GatewayChannelClose_RoutesCloseToRuntime()
    {
        var port = GetFreeTcpPort();
        var runtime = new RecordingWorldRuntime();
        var manager = CreateManager(port, runtime, new RecordingDirectConnectCodeValidator(MasterNodeKind.Backend));
        await manager.StartAsync(CancellationToken.None);

        using var client = new TcpClient();
        try
        {
            await client.ConnectAsync(IPAddress.Loopback, port);
            await using var stream = client.GetStream();
            await CompleteGatewayHandshakeAsync(stream);

            var close = new GatewayBackendChannelClose(456, "client disconnected");
            using (var frame = PacketCodec.Encode(
                       PacketKind.Notify,
                       Pid.GATE_BACKEND_CHANNEL_CLOSE,
                       GatewayBackendChannelClose.ProtocolVersion,
                       close,
                       GatewayBackendChannelClose.Codec))
            {
                await PacketFrameWriter.WriteAsync(stream, frame, CancellationToken.None);
            }

            var received = await runtime.Close.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("gateway-test", received.GatewayNodeId);
            Assert.Equal((uint)456, received.ChannelId);
            Assert.Equal("client disconnected", received.Reason);
        }
        finally
        {
            await manager.StopAsync(CancellationToken.None);
        }
    }

    private static GatewayConnectionManager CreateManager(
        int port,
        IDedicatedWorldRuntime runtime,
        IDirectConnectCodeValidator validator)
    {
        return new GatewayConnectionManager(
            Microsoft.Extensions.Options.Options.Create(new GatewayListenerOptions
            {
                IPAddress = "127.0.0.1",
                Port = port,
                UseTls = false,
                HandshakeTimeoutMilliseconds = 5000
            }),
            runtime,
            validator,
            new TestLogger<GatewayConnectionManager>());
    }

    private static async Task CompleteGatewayHandshakeAsync(Stream stream)
    {
        await ReadControlFrameAsync(stream, MasterControlPacketIds.NodeAuthChallenge, NodeAuthChallenge.Codec);
        await WriteControlFrameAsync(
            stream,
            MasterControlPacketIds.NodeHello,
            new NodeHello(
                MasterNodeKind.Gateway,
                "gateway-test",
                "Gateway Test",
                MasterControlProtocol.SchemaVersion,
                "gateway-master-a"),
            NodeHello.Codec);
        await WriteControlFrameAsync(
            stream,
            MasterControlPacketIds.DirectConnectCode,
            new DirectConnectCode("direct-code"),
            DirectConnectCode.Codec);
        using var acceptedFrame = await ReadRequiredFrameAsync(
            stream,
            MasterControlProtocol.UntrustedHandshakePolicy,
            CancellationToken.None);
        MasterControlProtocol.ValidateControlFrame(acceptedFrame, MasterControlPacketIds.NodeAccepted);
    }

    private static async Task<TPacket> ReadControlFrameAsync<TPacket>(
        Stream stream,
        ushort packetId,
        IPacketCodec<TPacket> codec)
    {
        using var frame = await ReadRequiredFrameAsync(
            stream,
            MasterControlProtocol.UntrustedHandshakePolicy,
            CancellationToken.None);
        MasterControlProtocol.ValidateControlFrame(frame, packetId);
        return PacketCodec.Decode(frame, codec);
    }

    private static async Task<PacketFrame> ReadRequiredFrameAsync(
        Stream stream,
        PacketReadPolicy policy,
        CancellationToken cancellationToken)
    {
        var frame = await PacketFrameReader.ReadAsync(stream, policy, cancellationToken);
        return frame ?? throw new EndOfStreamException("Dedicated Gateway test connection closed.");
    }

    private static async Task<PacketFrame?> TryReadFrameAsync(
        Stream stream,
        TimeSpan timeout)
    {
        using var timeoutCancellation = new CancellationTokenSource(timeout);
        try
        {
            return await PacketFrameReader.ReadAsync(
                stream,
                MasterControlProtocol.UntrustedHandshakePolicy,
                timeoutCancellation.Token);
        }
        catch (OperationCanceledException) when (timeoutCancellation.IsCancellationRequested)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static async Task WriteControlFrameAsync<TPacket>(
        Stream stream,
        ushort packetId,
        TPacket value,
        IPacketCodec<TPacket> codec)
    {
        using var frame = PacketCodec.Encode(
            PacketKind.Control,
            packetId,
            MasterControlProtocol.SchemaVersion,
            value,
            codec);
        await PacketFrameWriter.WriteAsync(stream, frame, CancellationToken.None);
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private sealed class RecordingDirectConnectCodeValidator(MasterNodeKind targetNodeKind) : IDirectConnectCodeValidator
    {
        public TaskCompletionSource<ValidationRequest> Validation { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<DirectConnectCodeValidationResponse> ValidateDirectConnectCodeAsync(
            string code,
            string gatewayNodeId,
            string gatewayMasterConnectionId,
            CancellationToken cancellationToken)
        {
            Validation.TrySetResult(new ValidationRequest(
                code,
                gatewayNodeId,
                gatewayMasterConnectionId));
            return Task.FromResult(new DirectConnectCodeValidationResponse(
                Guid.NewGuid(),
                success: true,
                gatewayNodeId,
                gatewayMasterConnectionId,
                targetNodeKind,
                "dedicated-local",
                "dedicated-master-a",
                string.Empty));
        }
    }

    private sealed record ValidationRequest(
        string Code,
        string GatewayNodeId,
        string GatewayMasterConnectionId);

    private sealed class RecordingWorldRuntime : IDedicatedWorldRuntime
    {
        public TaskCompletionSource<ReceivedPacket> Packet { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<DedicatedGatewayChannelCloseContext> Close { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask StartAsync(CancellationToken cancellationToken)
        {
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
            Packet.TrySetResult(new ReceivedPacket(context, payload.ToArray()));
            return ValueTask.CompletedTask;
        }

        public ValueTask HandleGatewayChannelClosedAsync(
            DedicatedGatewayChannelCloseContext context,
            CancellationToken cancellationToken)
        {
            Close.TrySetResult(context);
            return ValueTask.CompletedTask;
        }
    }

    private sealed record ReceivedPacket(
        DedicatedGatewayPacketContext Context,
        byte[] Payload);

    private sealed class TestLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return false;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
        }
    }
}
