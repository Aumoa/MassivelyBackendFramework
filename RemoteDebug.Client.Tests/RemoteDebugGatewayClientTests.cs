using System.Net;
using System.Net.Sockets;
using GatewayServer.Protocols;
using PacketCore;
using RemoteDebug;
using RemoteDebugServer.Protocols;
using Xunit;

namespace RemoteDebug.Client.Tests;

public sealed class RemoteDebugGatewayClientTests
{
    [Fact]
    public async Task ConnectHeartbeatAndDisconnect_RoutesClientSessionThroughGateway()
    {
        await using var server = FakeGatewayRouteServer.Start();

        await using var client = await RemoteDebugGatewayClient.ConnectAsync(new RemoteDebugGatewayClientOptions
        {
            Host = "127.0.0.1",
            Port = server.Port,
            UseTls = false,
            BackendKind = "UnityRemoteDebug",
            ClientId = "client-a",
            DisplayName = "Unity Editor",
            ClientVersion = "1.2.3",
            UnityVersion = "6000.0.1f1",
            Capabilities = RemoteDebugCapabilities.LogStreaming | RemoteDebugCapabilities.RemoteControl
        });

        Assert.Equal("client-a", client.Session.ClientId);
        Assert.Equal("session-a", client.Session.SessionId);
        Assert.Equal(RemoteDebugCapabilities.LogStreaming, client.Session.EnabledCapabilities);
        Assert.Equal(15000, client.Session.HeartbeatIntervalMilliseconds);

        var heartbeat = await client.HeartbeatAsync();
        Assert.Equal("client-a", heartbeat.ClientId);
        Assert.Equal("session-a", heartbeat.SessionId);
        Assert.Equal(2000, heartbeat.ObservedAtUnixTimeMilliseconds);

        await client.DisconnectAsync("test shutdown");

        var registerRequest = await server.RegisterRequest.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("client-a", registerRequest.ClientId);
        Assert.Equal("Unity Editor", registerRequest.DisplayName);
        Assert.Equal("1.2.3", registerRequest.ClientVersion);
        Assert.Equal("6000.0.1f1", registerRequest.UnityVersion);
        Assert.Equal(RemoteDebugCapabilities.LogStreaming | RemoteDebugCapabilities.RemoteControl, registerRequest.RequestedCapabilities);

        var heartbeatRequest = await server.HeartbeatRequest.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("client-a", heartbeatRequest.ClientId);
        Assert.Equal("session-a", heartbeatRequest.SessionId);

        var disconnectNotify = await server.DisconnectNotify.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("client-a", disconnectNotify.ClientId);
        Assert.Equal("session-a", disconnectNotify.SessionId);
        Assert.Equal("test shutdown", disconnectNotify.Reason);
    }

    [Fact]
    public async Task ConnectAsync_ThrowsWhenBackendReturnsError()
    {
        await using var server = FakeGatewayRouteServer.Start();
        server.RegisterErrorMessage = "registration rejected";

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => RemoteDebugGatewayClient.ConnectAsync(new RemoteDebugGatewayClientOptions
        {
            Host = "127.0.0.1",
            Port = server.Port,
            UseTls = false,
            BackendKind = "UnityRemoteDebug",
            ClientId = "client-a",
            DisplayName = "Unity Editor",
            ClientVersion = "1.2.3",
            UnityVersion = "6000.0.1f1",
            Capabilities = RemoteDebugCapabilities.LogStreaming
        }));

        Assert.Equal("registration rejected", error.Message);
    }

    private sealed class FakeGatewayRouteServer : IAsyncDisposable
    {
        private readonly TcpListener m_Listener;
        private readonly CancellationTokenSource m_Cancellation = new();
        private readonly Task m_RunTask;

        private FakeGatewayRouteServer(TcpListener listener)
        {
            m_Listener = listener;
            Port = ((IPEndPoint)m_Listener.LocalEndpoint).Port;
            m_RunTask = RunAsync(m_Cancellation.Token);
        }

        public int Port { get; }

        public string? RegisterErrorMessage { get; set; }

        public TaskCompletionSource<RemoteDebugBackendClientRegisterRequest> RegisterRequest { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<RemoteDebugBackendClientHeartbeatRequest> HeartbeatRequest { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<RemoteDebugBackendClientDisconnectNotify> DisconnectNotify { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public static FakeGatewayRouteServer Start()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return new FakeGatewayRouteServer(listener);
        }

        public async ValueTask DisposeAsync()
        {
            await m_Cancellation.CancelAsync();
            m_Listener.Stop();
            try
            {
                await m_RunTask;
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }

            m_Cancellation.Dispose();
        }

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            using var tcpClient = await m_Listener.AcceptTcpClientAsync(cancellationToken);
            await using var stream = tcpClient.GetStream();
            await WriteGatewayHandshakeAsync(stream, cancellationToken);

            while (!cancellationToken.IsCancellationRequested)
            {
                using var frame = await PacketFrameReader.ReadAsync(
                    stream,
                    PacketReadPolicy.TrustedServer,
                    cancellationToken);
                if (frame == null)
                {
                    return;
                }

                if (frame.Header.PacketId != Pid.GATE_BACKEND_ROUTE ||
                    frame.Header.Version != GatewayBackendRouteEnvelope.ProtocolVersion)
                {
                    throw new InvalidOperationException("Unexpected Gateway frame.");
                }

                var envelope = PacketCodec.Decode(frame, GatewayBackendRouteEnvelope.Codec);
                if (frame.Header.Kind != envelope.RoutedKind)
                {
                    throw new InvalidOperationException("Gateway route frame kind did not match the envelope.");
                }

                using var routedFrame = envelope.CreateRoutedFrame();
                switch (envelope.RoutedPacketId)
                {
                    case RemoteDebugPacketIds.BackendClientRegisterRequest:
                        await HandleRegisterAsync(stream, envelope, routedFrame, cancellationToken);
                        break;
                    case RemoteDebugPacketIds.BackendClientHeartbeatRequest:
                        await HandleHeartbeatAsync(stream, envelope, routedFrame, cancellationToken);
                        break;
                    case RemoteDebugPacketIds.BackendClientDisconnectNotify:
                        var notify = PacketCodec.Decode(routedFrame, RemoteDebugBackendClientDisconnectNotify.Codec);
                        DisconnectNotify.TrySetResult(notify);
                        return;
                    default:
                        throw new InvalidOperationException("Unexpected RemoteDebug packet id.");
                }
            }
        }

        private async Task HandleRegisterAsync(
            Stream stream,
            GatewayBackendRouteEnvelope envelope,
            PacketFrame routedFrame,
            CancellationToken cancellationToken)
        {
            var request = PacketCodec.Decode(routedFrame, RemoteDebugBackendClientRegisterRequest.Codec);
            RegisterRequest.TrySetResult(request);
            await WriteGatewayRouteAcceptedAsync(stream, envelope, cancellationToken);

            if (RegisterErrorMessage != null)
            {
                await WriteBackendRouteResponseAsync(
                    stream,
                    envelope,
                    RemoteDebugPacketIds.BackendErrorResponse,
                    new RemoteDebugBackendErrorResponse(
                        RemoteDebugPacketIds.BackendClientRegisterRequest,
                        RemoteDebugProtocol.SchemaVersion,
                        RegisterErrorMessage),
                    RemoteDebugBackendErrorResponse.Codec,
                    cancellationToken);
                return;
            }

            await WriteBackendRouteResponseAsync(
                stream,
                envelope,
                RemoteDebugPacketIds.BackendClientRegisterResponse,
                new RemoteDebugBackendClientRegisterResponse(
                    request.ClientId,
                    "session-a",
                    RemoteDebugCapabilities.LogStreaming,
                    heartbeatIntervalMilliseconds: 15000,
                    registeredAtUnixTimeMilliseconds: 1000),
                RemoteDebugBackendClientRegisterResponse.Codec,
                cancellationToken);
        }

        private async Task HandleHeartbeatAsync(
            Stream stream,
            GatewayBackendRouteEnvelope envelope,
            PacketFrame routedFrame,
            CancellationToken cancellationToken)
        {
            var request = PacketCodec.Decode(routedFrame, RemoteDebugBackendClientHeartbeatRequest.Codec);
            HeartbeatRequest.TrySetResult(request);
            await WriteGatewayRouteAcceptedAsync(stream, envelope, cancellationToken);
            await WriteBackendRouteResponseAsync(
                stream,
                envelope,
                RemoteDebugPacketIds.BackendClientHeartbeatResponse,
                new RemoteDebugBackendClientHeartbeatResponse(
                    request.ClientId,
                    request.SessionId,
                    observedAtUnixTimeMilliseconds: 2000),
                RemoteDebugBackendClientHeartbeatResponse.Codec,
                cancellationToken);
        }

        private static async Task WriteGatewayHandshakeAsync(
            Stream stream,
            CancellationToken cancellationToken)
        {
            using var handshakeFrame = PacketCodec.Encode(
                PacketKind.Notify,
                Pid.GATE_HANDSHAKE_NOTIFY,
                version: 1,
                new GatewayHandshakeNotify("https://accounts.example.test/authorize"),
                GatewayHandshakeNotify.Codec);
            await PacketFrameWriter.WriteAsync(stream, handshakeFrame, cancellationToken);
        }

        private static async Task WriteGatewayRouteAcceptedAsync(
            Stream stream,
            GatewayBackendRouteEnvelope envelope,
            CancellationToken cancellationToken)
        {
            using var acceptedFrame = PacketCodec.Encode(
                PacketKind.Response,
                Pid.GATE_BACKEND_ROUTE,
                GatewayBackendRouteEnvelope.ProtocolVersion,
                GatewayBackendRouteResponse.Accepted(envelope.RouteId, envelope.BackendKind),
                GatewayBackendRouteResponse.Codec);
            await PacketFrameWriter.WriteAsync(stream, acceptedFrame, cancellationToken);
        }

        private static async Task WriteBackendRouteResponseAsync<TResponse>(
            Stream stream,
            GatewayBackendRouteEnvelope requestEnvelope,
            ushort packetId,
            TResponse response,
            IPacketCodec<TResponse> responseCodec,
            CancellationToken cancellationToken)
        {
            using var responseFrame = PacketCodec.Encode(
                PacketKind.Response,
                packetId,
                RemoteDebugProtocol.SchemaVersion,
                response,
                responseCodec);
            var responseEnvelope = new GatewayBackendRouteEnvelope(
                requestEnvelope.BackendKind,
                requestEnvelope.RouteId,
                PacketKind.Response,
                packetId,
                RemoteDebugProtocol.SchemaVersion,
                responseFrame.Payload.ToArray());
            using var gatewayFrame = PacketCodec.Encode(
                PacketKind.Notify,
                Pid.GATE_BACKEND_ROUTE,
                GatewayBackendRouteEnvelope.ProtocolVersion,
                responseEnvelope,
                GatewayBackendRouteEnvelope.Codec);
            await PacketFrameWriter.WriteAsync(stream, gatewayFrame, cancellationToken);
        }
    }
}
