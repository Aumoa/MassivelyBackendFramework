using System.Net;
using System.Net.Sockets;
using BackendServer.Options;
using BackendServer.Services;
using MasterServer.ControlPlane;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PacketCore;
using Xunit;

namespace GatewayServer.Tests.Services;

public sealed class BackendMasterConnectionManagerTests
{
    [Fact]
    public async Task MasterConnection_WhenGatewayListenerDisabled_AdvertisesExternalEndpoint()
    {
        await using var master = new FakeMasterServer();
        var manager = new MasterConnectionManager(
            Microsoft.Extensions.Options.Options.Create(new MasterConnectionOptions
            {
                Enabled = true,
                IPAddress = "127.0.0.1",
                Port = master.Port,
                NodeId = "cpp-sidecar",
                DisplayName = "C++ Sidecar",
                BackendKind = "cpp-world",
                BackendPacketManifestId = "cpp-world:v1",
                BackendPacketManifestHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                SharedSecret = "test-secret",
                ReconnectDelayMilliseconds = 100,
                HandshakeTimeoutMilliseconds = 5000
            }),
            Microsoft.Extensions.Options.Options.Create(new GatewayListenerOptions
            {
                Enabled = false,
                IPAddress = "10.20.30.40",
                Port = 21701,
                UseTls = true
            }),
            new ServiceCollection().BuildServiceProvider(),
            new TestLogger<MasterConnectionManager>());
        await manager.StartAsync(CancellationToken.None);

        try
        {
            var hello = await master.Hello.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(MasterNodeKind.Backend, hello.NodeKind);
            Assert.Equal("cpp-sidecar", hello.NodeId);
            Assert.Equal("C++ Sidecar", hello.DisplayName);

            var advertise = await master.Advertise.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("cpp-world", advertise.BackendKind);
            Assert.Equal("10.20.30.40", advertise.GatewayEndpoint.IPAddress);
            Assert.Equal(21701, advertise.GatewayEndpoint.Port);
            Assert.True(advertise.GatewayEndpoint.UseTls);
            Assert.Equal("cpp-world:v1", advertise.ManifestId.Value);
            Assert.Equal("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", advertise.ManifestHash.Value);
        }
        finally
        {
            await manager.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task MasterConnection_WhenEndpointReadinessRequired_WaitsUntilReadyBeforeAdvertise()
    {
        await using var master = new FakeMasterServer();
        var sidecarPort = GetFreeTcpPort();
        var sidecar = new SidecarControlServer(
            Microsoft.Extensions.Options.Options.Create(new SidecarControlOptions
            {
                Enabled = true,
                RequireEndpointReadyBeforeAdvertise = true,
                IPAddress = "127.0.0.1",
                Port = sidecarPort
            }),
            new AlwaysSuccessfulDirectConnectCodeValidator(),
            new TestLogger<SidecarControlServer>());
        await sidecar.StartAsync(CancellationToken.None);

        var services = new ServiceCollection();
        services.AddSingleton<IBackendEndpointReadiness>(sidecar);
        var manager = new MasterConnectionManager(
            Microsoft.Extensions.Options.Options.Create(new MasterConnectionOptions
            {
                Enabled = true,
                IPAddress = "127.0.0.1",
                Port = master.Port,
                NodeId = "cpp-sidecar",
                DisplayName = "C++ Sidecar",
                BackendKind = "cpp-world",
                BackendPacketManifestId = "cpp-world:v1",
                BackendPacketManifestHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                SharedSecret = "test-secret",
                ReconnectDelayMilliseconds = 100,
                HandshakeTimeoutMilliseconds = 5000
            }),
            Microsoft.Extensions.Options.Options.Create(new GatewayListenerOptions
            {
                Enabled = false,
                IPAddress = "10.20.30.40",
                Port = 21701,
                UseTls = true
            }),
            services.BuildServiceProvider(),
            new TestLogger<MasterConnectionManager>());
        await manager.StartAsync(CancellationToken.None);

        try
        {
            await master.Hello.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.ThrowsAsync<TimeoutException>(async () =>
                await master.Advertise.Task.WaitAsync(TimeSpan.FromMilliseconds(300)));

            await SendEndpointReadyAsync(sidecarPort);

            var advertise = await master.Advertise.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("cpp-world", advertise.BackendKind);
            Assert.Equal("10.20.30.40", advertise.GatewayEndpoint.IPAddress);
            Assert.Equal(21701, advertise.GatewayEndpoint.Port);
        }
        finally
        {
            await manager.StopAsync(CancellationToken.None);
            await sidecar.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task MasterConnection_WhenManifestRequired_AdvertisesDeclaredManifest()
    {
        await using var master = new FakeMasterServer();
        var sidecarPort = GetFreeTcpPort();
        var sidecar = new SidecarControlServer(
            Microsoft.Extensions.Options.Options.Create(new SidecarControlOptions
            {
                Enabled = true,
                RequireManifestBeforeAdvertise = true,
                IPAddress = "127.0.0.1",
                Port = sidecarPort
            }),
            new AlwaysSuccessfulDirectConnectCodeValidator(),
            new TestLogger<SidecarControlServer>());
        await sidecar.StartAsync(CancellationToken.None);

        var services = new ServiceCollection();
        services.AddSingleton<IBackendManifestIdentityProvider>(sidecar);
        var manager = new MasterConnectionManager(
            Microsoft.Extensions.Options.Options.Create(new MasterConnectionOptions
            {
                Enabled = true,
                IPAddress = "127.0.0.1",
                Port = master.Port,
                NodeId = "cpp-sidecar",
                DisplayName = "C++ Sidecar",
                BackendKind = "cpp-world",
                BackendPacketManifestId = "cpp-world:v1",
                BackendPacketManifestHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                SharedSecret = "test-secret",
                ReconnectDelayMilliseconds = 100,
                HandshakeTimeoutMilliseconds = 5000
            }),
            Microsoft.Extensions.Options.Options.Create(new GatewayListenerOptions
            {
                Enabled = false,
                IPAddress = "10.20.30.40",
                Port = 21701,
                UseTls = true
            }),
            services.BuildServiceProvider(),
            new TestLogger<MasterConnectionManager>());
        await manager.StartAsync(CancellationToken.None);

        try
        {
            await master.Hello.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.ThrowsAsync<TimeoutException>(async () =>
                await master.Advertise.Task.WaitAsync(TimeSpan.FromMilliseconds(300)));

            await SendManifestDeclarationAsync(sidecarPort);

            var advertise = await master.Advertise.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("cpp-world:v2", advertise.ManifestId.Value);
            Assert.Equal("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", advertise.ManifestHash.Value);
        }
        finally
        {
            await manager.StopAsync(CancellationToken.None);
            await sidecar.StopAsync(CancellationToken.None);
        }
    }

    private static async Task SendEndpointReadyAsync(int port)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        await using var stream = client.GetStream();
        var update = new SidecarEndpointStateUpdate(
            Guid.NewGuid(),
            ready: true,
            "cpp listener ready");
        using (var frame = PacketCodec.Encode(
                   PacketKind.Control,
                   BackendSidecarControlPacketIds.EndpointStateUpdate,
                   BackendSidecarControlProtocol.SchemaVersion,
                   update,
                   SidecarEndpointStateUpdate.Codec))
        {
            await PacketFrameWriter.WriteAsync(stream, frame, CancellationToken.None);
        }

        using var ackFrame = await PacketFrameReader.ReadAsync(
            stream,
            BackendSidecarControlProtocol.LocalControlPolicy,
            CancellationToken.None) ?? throw new EndOfStreamException("Sidecar endpoint state ack was not written.");
        BackendSidecarControlProtocol.ValidateControlFrame(
            ackFrame,
            BackendSidecarControlPacketIds.EndpointStateAck);
        var ack = PacketCodec.Decode(ackFrame, SidecarEndpointStateAck.Codec);
        Assert.True(ack.Success);
        Assert.Equal(update.RequestId, ack.RequestId);
    }

    private static async Task SendManifestDeclarationAsync(int port)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        await using var stream = client.GetStream();
        var update = new SidecarManifestDeclarationUpdate(
            Guid.NewGuid(),
            "cpp-world:v2",
            "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");
        using (var frame = PacketCodec.Encode(
                   PacketKind.Control,
                   BackendSidecarControlPacketIds.ManifestDeclarationUpdate,
                   BackendSidecarControlProtocol.SchemaVersion,
                   update,
                   SidecarManifestDeclarationUpdate.Codec))
        {
            await PacketFrameWriter.WriteAsync(stream, frame, CancellationToken.None);
        }

        using var ackFrame = await PacketFrameReader.ReadAsync(
            stream,
            BackendSidecarControlProtocol.LocalControlPolicy,
            CancellationToken.None) ?? throw new EndOfStreamException("Sidecar manifest declaration ack was not written.");
        BackendSidecarControlProtocol.ValidateControlFrame(
            ackFrame,
            BackendSidecarControlPacketIds.ManifestDeclarationAck);
        var ack = PacketCodec.Decode(ackFrame, SidecarManifestDeclarationAck.Codec);
        Assert.True(ack.Success);
        Assert.Equal(update.RequestId, ack.RequestId);
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

    private sealed class FakeMasterServer : IAsyncDisposable
    {
        private readonly TcpListener m_Listener;
        private readonly CancellationTokenSource m_Shutdown = new();
        private readonly Task m_RunTask;

        public FakeMasterServer()
        {
            m_Listener = new TcpListener(IPAddress.Loopback, 0);
            m_Listener.Start();
            Port = ((IPEndPoint)m_Listener.LocalEndpoint).Port;
            m_RunTask = RunAsync(m_Shutdown.Token);
        }

        public int Port { get; }

        public TaskCompletionSource<NodeHello> Hello { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<BackendEndpointAdvertise> Advertise { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask DisposeAsync()
        {
            await m_Shutdown.CancelAsync();
            m_Listener.Stop();

            try
            {
                await m_RunTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }

            m_Shutdown.Dispose();
        }

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            try
            {
                using var socket = await m_Listener.AcceptSocketAsync(cancellationToken).ConfigureAwait(false);
                await using var stream = new NetworkStream(socket, ownsSocket: false);
                var challenge = new NodeAuthChallenge(
                    "test-challenge",
                    new byte[MasterControlProtocol.AuthNonceLength]);

                await WriteControlFrameAsync(
                    stream,
                    MasterControlPacketIds.NodeAuthChallenge,
                    challenge,
                    NodeAuthChallenge.Codec,
                    cancellationToken).ConfigureAwait(false);

                var hello = await ReadControlFrameAsync(
                    stream,
                    MasterControlPacketIds.NodeHello,
                    NodeHello.Codec,
                    MasterControlProtocol.UntrustedHandshakePolicy,
                    cancellationToken).ConfigureAwait(false);
                Hello.TrySetResult(hello);

                _ = await ReadControlFrameAsync(
                    stream,
                    MasterControlPacketIds.NodeAuthProof,
                    NodeAuthProof.Codec,
                    MasterControlProtocol.UntrustedHandshakePolicy,
                    cancellationToken).ConfigureAwait(false);

                await WriteControlFrameAsync(
                    stream,
                    MasterControlPacketIds.NodeAccepted,
                    new NodeAccepted(hello.NodeId, "master-connection-a"),
                    NodeAccepted.Codec,
                    cancellationToken).ConfigureAwait(false);

                var advertise = await ReadControlFrameAsync(
                    stream,
                    MasterControlPacketIds.BackendEndpointAdvertise,
                    BackendEndpointAdvertise.Codec,
                    MasterControlProtocol.TrustedControlPlanePolicy,
                    cancellationToken).ConfigureAwait(false);
                Advertise.TrySetResult(advertise);

                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                Hello.TrySetException(exception);
                Advertise.TrySetException(exception);
            }
        }

        private static async Task WriteControlFrameAsync<TPacket>(
            Stream stream,
            ushort packetId,
            TPacket value,
            IPacketCodec<TPacket> codec,
            CancellationToken cancellationToken)
        {
            using var frame = PacketCodec.Encode(
                PacketKind.Control,
                packetId,
                MasterControlProtocol.SchemaVersion,
                value,
                codec);
            await PacketFrameWriter.WriteAsync(stream, frame, cancellationToken).ConfigureAwait(false);
        }

        private static async Task<TPacket> ReadControlFrameAsync<TPacket>(
            Stream stream,
            ushort packetId,
            IPacketCodec<TPacket> codec,
            PacketReadPolicy policy,
            CancellationToken cancellationToken)
        {
            using var frame = await PacketFrameReader.ReadAsync(stream, policy, cancellationToken).ConfigureAwait(false) ??
                              throw new EndOfStreamException("Backend sidecar test connection closed.");
            MasterControlProtocol.ValidateControlFrame(frame, packetId);
            return PacketCodec.Decode(frame, codec);
        }
    }

    private sealed class AlwaysSuccessfulDirectConnectCodeValidator : IDirectConnectCodeValidator
    {
        public Task<DirectConnectCodeValidationResponse> ValidateDirectConnectCodeAsync(
            string code,
            string gatewayNodeId,
            string gatewayMasterConnectionId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new DirectConnectCodeValidationResponse(
                Guid.NewGuid(),
                success: true,
                gatewayNodeId,
                gatewayMasterConnectionId,
                MasterNodeKind.Backend,
                "backend-local",
                "backend-master-a",
                string.Empty));
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
