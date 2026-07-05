using System.Net;
using System.Net.Sockets;
using BackendServer.Options;
using BackendServer.Runtime;
using BackendServer.Services;
using GatewayServer.Protocols;
using MasterServer.ControlPlane;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PacketCore;
using Xunit;

namespace GatewayServer.Tests.Services;

public sealed class BackendGatewayConnectionManagerTests
{
    [Fact]
    public async Task StartAsync_WhenListenerDisabled_LeavesPortForExternalDataPlane()
    {
        var port = GetFreeTcpPort();
        var manager = CreateManager(
            new GatewayListenerOptions
            {
                Enabled = false,
                IPAddress = "127.0.0.1",
                Port = port,
                UseTls = true,
                CertificateSubjectName = "missing-cpp-sidecar-cert"
            },
            new RecordingWorldRuntime(),
            new RecordingDirectConnectCodeValidator(MasterNodeKind.Backend));
        await manager.StartAsync(CancellationToken.None);

        try
        {
            var status = manager.GetStatusItems();
            Assert.Contains(status, item =>
                item.Group == "Gateway" &&
                item.Name == "Listener" &&
                item.Value == "Disabled");
            Assert.Contains(status, item =>
                item.Group == "Gateway" &&
                item.Name == "Data plane owner" &&
                item.Value == "External");
            Assert.Contains(status, item =>
                item.Group == "Gateway" &&
                item.Name == "Advertised endpoint" &&
                item.Value == $"127.0.0.1:{port}");
            Assert.Contains(status, item =>
                item.Group == "Gateway" &&
                item.Name == "TLS" &&
                item.Value == "Enabled");

            var externalDataPlane = new TcpListener(IPAddress.Loopback, port);
            externalDataPlane.Start();
            externalDataPlane.Stop();
        }
        finally
        {
            await manager.StopAsync(CancellationToken.None);
        }
    }

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
            Assert.Null(received.Context.ExchangeId);
            Assert.Equal(payload, received.Payload);
        }
        finally
        {
            await manager.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task GatewayHandshake_DropsChannelDataWhenFrameKindDiffersFromRoutedKind()
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

            var mismatchedEnvelope = new GatewayBackendChannelDataEnvelope(
                channelId: 123,
                PacketKind.Response,
                routedPacketId: 501,
                routedVersion: 2,
                new GatewayBackendExchangeId(Guid.NewGuid()),
                [7, 8, 9]);
            using (var frame = PacketCodec.Encode(
                       PacketKind.Notify,
                       Pid.GATE_BACKEND_CHANNEL_DATA,
                       GatewayBackendChannelDataEnvelope.ProtocolVersion,
                       mismatchedEnvelope,
                       GatewayBackendChannelDataEnvelope.Codec))
            {
                await PacketFrameWriter.WriteAsync(stream, frame, CancellationToken.None);
            }

            byte[] payload = [1, 2, 3];
            var validEnvelope = new GatewayBackendChannelDataEnvelope(
                channelId: 124,
                PacketKind.Notify,
                routedPacketId: 502,
                routedVersion: 3,
                exchangeId: null,
                payload);
            using (var frame = PacketCodec.Encode(
                       PacketKind.Notify,
                       Pid.GATE_BACKEND_CHANNEL_DATA,
                       GatewayBackendChannelDataEnvelope.ProtocolVersion,
                       validEnvelope,
                       GatewayBackendChannelDataEnvelope.Codec))
            {
                await PacketFrameWriter.WriteAsync(stream, frame, CancellationToken.None);
            }

            var received = await runtime.Packet.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal((uint)124, received.Context.ChannelId);
            Assert.Equal(PacketKind.Notify, received.Context.Kind);
            Assert.Equal((ushort)502, received.Context.PacketId);
            Assert.Equal((ushort)3, received.Context.Version);
            Assert.Null(received.Context.ExchangeId);
            Assert.Equal(payload, received.Payload);
        }
        finally
        {
            await manager.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task GatewayHandshake_RoutesRequestExchangeIdToRuntime()
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
            var accepted = await CompleteGatewayHandshakeAsync(stream);

            var exchangeId = Guid.NewGuid();
            byte[] payload = [7, 8, 9];
            var envelope = new GatewayBackendChannelDataEnvelope(
                channelId: 123,
                PacketKind.Request,
                routedPacketId: 501,
                routedVersion: 2,
                new GatewayBackendExchangeId(exchangeId),
                payload);
            using (var frame = PacketCodec.Encode(
                       PacketKind.Request,
                       Pid.GATE_BACKEND_CHANNEL_DATA,
                       GatewayBackendChannelDataEnvelope.ProtocolVersion,
                       envelope,
                       GatewayBackendChannelDataEnvelope.Codec))
            {
                await PacketFrameWriter.WriteAsync(stream, frame, CancellationToken.None);
            }

            var received = await runtime.Packet.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(Guid.ParseExact(accepted.ConnectionId, "N"), received.Context.GatewayConnectionId);
            Assert.Equal(new BackendGatewayChannel(received.Context.GatewayConnectionId, 123), received.Context.Channel);
            Assert.Equal(PacketKind.Request, received.Context.Kind);
            Assert.Equal(exchangeId, received.Context.ExchangeId);
            Assert.Equal(payload, received.Payload);
        }
        finally
        {
            await manager.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task GatewayChannelOpen_RoutesOpenToRuntime()
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
            var accepted = await CompleteGatewayHandshakeAsync(stream);

            var open = new GatewayBackendChannelOpen(456, "player-1");
            using (var frame = PacketCodec.Encode(
                       PacketKind.Notify,
                       Pid.GATE_BACKEND_CHANNEL_OPEN,
                       GatewayBackendChannelOpen.ProtocolVersion,
                       open,
                       GatewayBackendChannelOpen.Codec))
            {
                await PacketFrameWriter.WriteAsync(stream, frame, CancellationToken.None);
            }

            var received = await runtime.Open.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("gateway-test", received.GatewayNodeId);
            Assert.Equal(Guid.ParseExact(accepted.ConnectionId, "N"), received.GatewayConnectionId);
            Assert.Equal((uint)456, received.ChannelId);
            Assert.Equal(new BackendGatewayChannel(received.GatewayConnectionId, 456), received.Channel);
            Assert.Equal("player-1", received.PrincipalSubjectId);
        }
        finally
        {
            await manager.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task GatewayChannelOpen_AllowsRuntimeToPushImmediately()
    {
        var port = GetFreeTcpPort();
        var sender = new GatewayChannelSender(new TestLogger<GatewayChannelSender>());
        var runtime = new OpenPushRuntime(sender);
        var manager = CreateManager(
            port,
            runtime,
            new RecordingDirectConnectCodeValidator(MasterNodeKind.Backend),
            sender);
        await manager.StartAsync(CancellationToken.None);

        using var client = new TcpClient();
        try
        {
            await client.ConnectAsync(IPAddress.Loopback, port);
            await using var stream = client.GetStream();
            await CompleteGatewayHandshakeAsync(stream);

            var open = new GatewayBackendChannelOpen(456, "player-1");
            using (var frame = PacketCodec.Encode(
                       PacketKind.Notify,
                       Pid.GATE_BACKEND_CHANNEL_OPEN,
                       GatewayBackendChannelOpen.ProtocolVersion,
                       open,
                       GatewayBackendChannelOpen.Codec))
            {
                await PacketFrameWriter.WriteAsync(stream, frame, CancellationToken.None);
            }

            using var pushedFrame = await ReadRequiredFrameAsync(
                stream,
                MasterControlProtocol.TrustedControlPlanePolicy,
                CancellationToken.None);
            Assert.Equal(PacketKind.Notify, pushedFrame.Header.Kind);
            Assert.Equal(Pid.GATE_BACKEND_CHANNEL_DATA, pushedFrame.Header.PacketId);

            var pushed = PacketCodec.Decode(pushedFrame, GatewayBackendChannelDataEnvelope.Codec);
            Assert.Equal((uint)456, pushed.ChannelId);
            Assert.Equal(PacketKind.Notify, pushed.RoutedKind);
            Assert.Equal((ushort)777, pushed.RoutedPacketId);
            Assert.Equal((ushort)1, pushed.RoutedVersion);
            Assert.Equal(new byte[] { 1, 2, 3 }, pushed.RoutedPayload);
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
                "Backend listener must not accept direct-connect tickets issued for the legacy Dedicated target kind.");
            Assert.False(runtime.Packet.Task.IsCompleted);
        }
        finally
        {
            await manager.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task GatewayChannelSender_SendNotify_WritesChannelDataToTrustedGateway()
    {
        var port = GetFreeTcpPort();
        var runtime = new RecordingWorldRuntime();
        var validator = new RecordingDirectConnectCodeValidator(MasterNodeKind.Backend);
        var sender = new GatewayChannelSender(new TestLogger<GatewayChannelSender>());
        var manager = CreateManager(port, runtime, validator, sender);
        await manager.StartAsync(CancellationToken.None);

        using var client = new TcpClient();
        try
        {
            await client.ConnectAsync(IPAddress.Loopback, port);
            await using var stream = client.GetStream();
            var accepted = await CompleteGatewayHandshakeAsync(stream);
            var channel = new BackendGatewayChannel(Guid.ParseExact(accepted.ConnectionId, "N"), 321);
            byte[] payload = [4, 5, 6];

            await sender.SendNotifyAsync(
                channel,
                packetId: 601,
                version: 3,
                payload,
                CancellationToken.None);

            using var frame = await ReadRequiredFrameAsync(
                stream,
                MasterControlProtocol.TrustedControlPlanePolicy,
                CancellationToken.None);
            Assert.Equal(PacketKind.Notify, frame.Header.Kind);
            Assert.Equal(Pid.GATE_BACKEND_CHANNEL_DATA, frame.Header.PacketId);
            Assert.Equal(GatewayBackendChannelDataEnvelope.ProtocolVersion, frame.Header.Version);

            var envelope = PacketCodec.Decode(frame, GatewayBackendChannelDataEnvelope.Codec);
            Assert.Equal(channel.ChannelId, envelope.ChannelId);
            Assert.Equal(PacketKind.Notify, envelope.RoutedKind);
            Assert.Equal((ushort)601, envelope.RoutedPacketId);
            Assert.Equal((ushort)3, envelope.RoutedVersion);
            Assert.False(envelope.ExchangeId.HasValue);
            Assert.Equal(payload, envelope.RoutedPayload);
        }
        finally
        {
            await manager.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task GatewayChannelSender_SendResponse_WritesExchangeIdToTrustedGateway()
    {
        var port = GetFreeTcpPort();
        var runtime = new RecordingWorldRuntime();
        var validator = new RecordingDirectConnectCodeValidator(MasterNodeKind.Backend);
        var sender = new GatewayChannelSender(new TestLogger<GatewayChannelSender>());
        var manager = CreateManager(port, runtime, validator, sender);
        await manager.StartAsync(CancellationToken.None);

        using var client = new TcpClient();
        try
        {
            await client.ConnectAsync(IPAddress.Loopback, port);
            await using var stream = client.GetStream();
            var accepted = await CompleteGatewayHandshakeAsync(stream);
            var channel = new BackendGatewayChannel(Guid.ParseExact(accepted.ConnectionId, "N"), 321);
            var exchangeId = Guid.NewGuid();
            byte[] payload = [9, 8, 7];

            await sender.SendResponseAsync(
                channel,
                packetId: 701,
                version: 4,
                exchangeId,
                payload,
                CancellationToken.None);

            using var frame = await ReadRequiredFrameAsync(
                stream,
                MasterControlProtocol.TrustedControlPlanePolicy,
                CancellationToken.None);
            Assert.Equal(PacketKind.Response, frame.Header.Kind);
            Assert.Equal(Pid.GATE_BACKEND_CHANNEL_DATA, frame.Header.PacketId);

            var envelope = PacketCodec.Decode(frame, GatewayBackendChannelDataEnvelope.Codec);
            Assert.Equal(channel.ChannelId, envelope.ChannelId);
            Assert.Equal(PacketKind.Response, envelope.RoutedKind);
            Assert.Equal((ushort)701, envelope.RoutedPacketId);
            Assert.Equal((ushort)4, envelope.RoutedVersion);
            Assert.True(envelope.ExchangeId.HasValue);
            Assert.Equal(exchangeId, envelope.ExchangeId.Value.Value);
            Assert.Equal(payload, envelope.RoutedPayload);
        }
        finally
        {
            await manager.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task GatewayChannelSender_Close_WritesChannelCloseToTrustedGateway()
    {
        var port = GetFreeTcpPort();
        var runtime = new RecordingWorldRuntime();
        var validator = new RecordingDirectConnectCodeValidator(MasterNodeKind.Backend);
        var sender = new GatewayChannelSender(new TestLogger<GatewayChannelSender>());
        var manager = CreateManager(port, runtime, validator, sender);
        await manager.StartAsync(CancellationToken.None);

        using var client = new TcpClient();
        try
        {
            await client.ConnectAsync(IPAddress.Loopback, port);
            await using var stream = client.GetStream();
            var accepted = await CompleteGatewayHandshakeAsync(stream);
            var channel = new BackendGatewayChannel(Guid.ParseExact(accepted.ConnectionId, "N"), 321);

            await sender.CloseAsync(channel, "backend shutdown", CancellationToken.None);

            using var frame = await ReadRequiredFrameAsync(
                stream,
                MasterControlProtocol.TrustedControlPlanePolicy,
                CancellationToken.None);
            Assert.Equal(PacketKind.Notify, frame.Header.Kind);
            Assert.Equal(Pid.GATE_BACKEND_CHANNEL_CLOSE, frame.Header.PacketId);
            Assert.Equal(GatewayBackendChannelClose.ProtocolVersion, frame.Header.Version);

            var close = PacketCodec.Decode(frame, GatewayBackendChannelClose.Codec);
            Assert.Equal(channel.ChannelId, close.ChannelId);
            Assert.Equal("backend shutdown", close.Reason);
        }
        finally
        {
            await manager.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task GatewayChannelSender_RejectsUnknownGatewayConnection()
    {
        var sender = new GatewayChannelSender(new TestLogger<GatewayChannelSender>());
        var channel = new BackendGatewayChannel(Guid.NewGuid(), 321);

        await Assert.ThrowsAsync<InvalidOperationException>(() => sender
            .SendNotifyAsync(
                channel,
                packetId: 601,
                version: 3,
                new byte[] { 1, 2, 3 },
                CancellationToken.None)
            .AsTask());
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
        IBackendRuntime runtime,
        IDirectConnectCodeValidator validator,
        GatewayChannelSender? sender = null)
    {
        return CreateManager(
            new GatewayListenerOptions
            {
                IPAddress = "127.0.0.1",
                Port = port,
                UseTls = false,
                HandshakeTimeoutMilliseconds = 5000
            },
            runtime,
            validator,
            sender);
    }

    private static GatewayConnectionManager CreateManager(
        GatewayListenerOptions options,
        IBackendRuntime runtime,
        IDirectConnectCodeValidator validator,
        GatewayChannelSender? sender = null)
    {
        return new GatewayConnectionManager(
            Microsoft.Extensions.Options.Options.Create(options),
            runtime,
            validator,
            sender ?? new GatewayChannelSender(new TestLogger<GatewayChannelSender>()),
            new TestLogger<GatewayConnectionManager>());
    }

    private static async Task<NodeAccepted> CompleteGatewayHandshakeAsync(Stream stream)
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
        return PacketCodec.Decode(acceptedFrame, NodeAccepted.Codec);
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
        return frame ?? throw new EndOfStreamException("Backend Gateway test connection closed.");
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
                "backend-local",
                "backend-master-a",
                string.Empty));
        }
    }

    private sealed record ValidationRequest(
        string Code,
        string GatewayNodeId,
        string GatewayMasterConnectionId);

    private sealed class RecordingWorldRuntime : IBackendRuntime
    {
        public TaskCompletionSource<BackendGatewayChannelOpenContext> Open { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<ReceivedPacket> Packet { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<BackendGatewayChannelCloseContext> Close { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask StartAsync(CancellationToken cancellationToken)
        {
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
            Open.TrySetResult(context);
            return ValueTask.CompletedTask;
        }

        public ValueTask HandleGatewayPacketAsync(
            BackendGatewayPacketContext context,
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken)
        {
            Packet.TrySetResult(new ReceivedPacket(context, payload.ToArray()));
            return ValueTask.CompletedTask;
        }

        public ValueTask HandleGatewayChannelClosedAsync(
            BackendGatewayChannelCloseContext context,
            CancellationToken cancellationToken)
        {
            Close.TrySetResult(context);
            return ValueTask.CompletedTask;
        }
    }

    private sealed record ReceivedPacket(
        BackendGatewayPacketContext Context,
        byte[] Payload);

    private sealed class OpenPushRuntime(IBackendGatewayChannelSender sender) : IBackendRuntime
    {
        public ValueTask StartAsync(CancellationToken cancellationToken)
        {
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
            return sender.SendNotifyAsync(
                context.Channel,
                packetId: 777,
                version: 1,
                new byte[] { 1, 2, 3 },
                cancellationToken);
        }

        public ValueTask HandleGatewayPacketAsync(
            BackendGatewayPacketContext context,
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask HandleGatewayChannelClosedAsync(
            BackendGatewayChannelCloseContext context,
            CancellationToken cancellationToken)
        {
            return ValueTask.CompletedTask;
        }
    }

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
