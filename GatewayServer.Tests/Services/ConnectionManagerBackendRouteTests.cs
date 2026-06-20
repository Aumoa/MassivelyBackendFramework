using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using GatewayServer.Options;
using GatewayServer.Protocols;
using GatewayServer.Services;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using PacketCore;
using Xunit;

namespace GatewayServer.Tests.Services;

public sealed class ConnectionManagerBackendRouteTests
{
    [Fact]
    public async Task ClientRouteRequest_RelaysToBackendAndForwardsBackendResponse()
    {
        var routeManager = new RecordingBackendRouteManager();
        var connectionManager = CreateConnectionManager(
            routeManager,
            new BackendRouteOptions
            {
                AllowedBackendKinds = ["alpha"],
                RequestTimeoutMilliseconds = 5000,
                MaxPendingRoutes = 8,
                MaxPendingRoutesPerClient = 2
            },
            out var port);

        await connectionManager.StartAsync(CancellationToken.None);

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);
            await using var stream = client.GetStream();

            using (var handshakeFrame = await ReadRequiredFrameAsync(stream))
            {
                Assert.Equal(PacketKind.Notify, handshakeFrame.Header.Kind);
                Assert.Equal(Pid.GATE_HANDSHAKE_NOTIFY, handshakeFrame.Header.PacketId);

                var handshake = PacketCodec.Decode(handshakeFrame, GatewayHandshakeNotify.Codec);
                Assert.Equal("https://accounts.ayla.r-e.kr/authorize", handshake.LoginUri);
            }

            var routeId = Guid.NewGuid();
            var requestPayload = new byte[] { 1, 2, 3, 4 };
            var requestEnvelope = new GatewayBackendRouteEnvelope(
                "alpha",
                routeId,
                PacketKind.Request,
                routedPacketId: 101,
                routedVersion: 2,
                requestPayload);
            await WriteBackendRouteEnvelopeAsync(stream, PacketKind.Request, requestEnvelope);

            var relayed = await routeManager.RelayedFrame.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("alpha", relayed.BackendKind);
            Assert.Equal(routeId, relayed.Envelope.RouteId);
            Assert.Equal(PacketKind.Request, relayed.Envelope.RoutedKind);
            Assert.Equal((ushort)101, relayed.Envelope.RoutedPacketId);
            Assert.Equal((ushort)2, relayed.Envelope.RoutedVersion);
            Assert.Equal(requestPayload, relayed.Envelope.RoutedPayload);

            using (var acceptedFrame = await ReadRequiredFrameAsync(stream))
            {
                Assert.Equal(PacketKind.Response, acceptedFrame.Header.Kind);
                Assert.Equal(Pid.GATE_BACKEND_ROUTE, acceptedFrame.Header.PacketId);

                var accepted = PacketCodec.Decode(acceptedFrame, GatewayBackendRouteResponse.Codec);
                Assert.True(accepted.Success);
                Assert.Equal("alpha", accepted.BackendKind);
                Assert.Equal(routeId, accepted.RouteId);
                Assert.Equal(string.Empty, accepted.ErrorMessage);
            }

            var responsePayload = new byte[] { 9, 8, 7 };
            await routeManager.PublishBackendRouteFrameAsync(new GatewayBackendRouteEnvelope(
                "alpha",
                routeId,
                PacketKind.Response,
                routedPacketId: 201,
                routedVersion: 3,
                responsePayload));

            using (var backendFrame = await ReadRequiredFrameAsync(stream))
            {
                Assert.Equal(PacketKind.Notify, backendFrame.Header.Kind);
                Assert.Equal(Pid.GATE_BACKEND_ROUTE, backendFrame.Header.PacketId);
                Assert.Equal(GatewayBackendRouteEnvelope.ProtocolVersion, backendFrame.Header.Version);

                var backendEnvelope = PacketCodec.Decode(backendFrame, GatewayBackendRouteEnvelope.Codec);
                Assert.Equal("alpha", backendEnvelope.BackendKind);
                Assert.Equal(routeId, backendEnvelope.RouteId);
                Assert.Equal(PacketKind.Response, backendEnvelope.RoutedKind);
                Assert.Equal((ushort)201, backendEnvelope.RoutedPacketId);
                Assert.Equal((ushort)3, backendEnvelope.RoutedVersion);
                Assert.Equal(responsePayload, backendEnvelope.RoutedPayload);
            }

            Assert.Contains(connectionManager.GetStatusItems(), item =>
                item.Group == "Backend routes" &&
                item.Name == "Pending routes" &&
                item.Value == "0");
        }
        finally
        {
            await connectionManager.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task ClientRouteRequest_RejectsBackendKindWhenNotAllowlisted()
    {
        var routeManager = new RecordingBackendRouteManager();
        var connectionManager = CreateConnectionManager(
            routeManager,
            new BackendRouteOptions
            {
                AllowedBackendKinds = [],
                RequestTimeoutMilliseconds = 5000
            },
            out var port);

        await connectionManager.StartAsync(CancellationToken.None);

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);
            await using var stream = client.GetStream();
            using (await ReadRequiredFrameAsync(stream))
            {
            }

            var routeId = Guid.NewGuid();
            var requestEnvelope = new GatewayBackendRouteEnvelope(
                "alpha",
                routeId,
                PacketKind.Request,
                routedPacketId: 101,
                routedVersion: 2,
                [1, 2, 3]);
            await WriteBackendRouteEnvelopeAsync(stream, PacketKind.Request, requestEnvelope);

            using var rejectedFrame = await ReadRequiredFrameAsync(stream);
            Assert.Equal(PacketKind.Response, rejectedFrame.Header.Kind);
            Assert.Equal(Pid.GATE_BACKEND_ROUTE, rejectedFrame.Header.PacketId);

            var rejected = PacketCodec.Decode(rejectedFrame, GatewayBackendRouteResponse.Codec);
            Assert.False(rejected.Success);
            Assert.Equal("alpha", rejected.BackendKind);
            Assert.Equal(routeId, rejected.RouteId);
            Assert.Equal("Backend route was rejected.", rejected.ErrorMessage);
            Assert.Equal(0, routeManager.RelayCount);
        }
        finally
        {
            await connectionManager.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task StartAsync_RejectsPlaintextConfiguration()
    {
        var routeManager = new RecordingBackendRouteManager();
        var connectionManager = CreateConnectionManager(
            routeManager,
            new BackendRouteOptions
            {
                AllowedBackendKinds = ["alpha"]
            },
            out _,
            connectionOptions: new ConnectionManagerOptions
            {
                IPAddress = "127.0.0.1",
                UseTls = false
            });

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await connectionManager.StartAsync(CancellationToken.None));
    }

    [Fact]
    public async Task StartAsync_FailsWhenTlsCertificateCannotBeLoaded()
    {
        var routeManager = new RecordingBackendRouteManager();
        var expected = new InvalidOperationException("missing certificate");
        var connectionManager = CreateConnectionManager(
            routeManager,
            new BackendRouteOptions
            {
                AllowedBackendKinds = ["alpha"]
            },
            out _,
            certificateLoader: new ThrowingCertificateLoader(expected));

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await connectionManager.StartAsync(CancellationToken.None));

        Assert.Same(expected, actual);
    }

    [Fact]
    public async Task StartAsync_AllowsUntrustedCertificateOnlyInDevelopment()
    {
        var routeManager = new RecordingBackendRouteManager();
        var certificateLoader = new StaticCertificateLoader(CreateServerCertificate());
        var connectionManager = CreateConnectionManager(
            routeManager,
            new BackendRouteOptions
            {
                AllowedBackendKinds = ["alpha"]
            },
            out _,
            hostEnvironment: new TestHostEnvironment
            {
                EnvironmentName = Environments.Development
            },
            certificateLoader: certificateLoader);

        await connectionManager.StartAsync(CancellationToken.None);

        try
        {
            Assert.True(certificateLoader.AllowUntrustedCertificate);
        }
        finally
        {
            await connectionManager.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task StartAsync_RequiresTrustedCertificateOutsideDevelopment()
    {
        var routeManager = new RecordingBackendRouteManager();
        var certificateLoader = new StaticCertificateLoader(CreateServerCertificate());
        var connectionManager = CreateConnectionManager(
            routeManager,
            new BackendRouteOptions
            {
                AllowedBackendKinds = ["alpha"]
            },
            out _,
            hostEnvironment: new TestHostEnvironment
            {
                EnvironmentName = Environments.Production
            },
            certificateLoader: certificateLoader);

        await connectionManager.StartAsync(CancellationToken.None);

        try
        {
            Assert.False(certificateLoader.AllowUntrustedCertificate);
        }
        finally
        {
            await connectionManager.StopAsync(CancellationToken.None);
        }
    }

    private static ConnectionManager CreateConnectionManager(
        RecordingBackendRouteManager routeManager,
        BackendRouteOptions backendRouteOptions,
        out int port,
        ConnectionManagerOptions? connectionOptions = null,
        IHostEnvironment? hostEnvironment = null,
        IGatewayClientCertificateLoader? certificateLoader = null,
        IGatewayClientStreamAuthenticator? streamAuthenticator = null)
    {
        port = GetAvailableTcpPort();
        connectionOptions ??= new ConnectionManagerOptions
        {
            IPAddress = "127.0.0.1"
        };
        connectionOptions.Port = port;
        return new ConnectionManager(
            Microsoft.Extensions.Options.Options.Create(connectionOptions),
            Microsoft.Extensions.Options.Options.Create(backendRouteOptions),
            routeManager,
            certificateLoader ?? new StaticCertificateLoader(CreateServerCertificate()),
            streamAuthenticator ?? new PassThroughStreamAuthenticator(),
            NullLogger<ConnectionManager>.Instance,
            hostEnvironment ?? new TestHostEnvironment());
    }

    private static int GetAvailableTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        try
        {
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task<PacketFrame> ReadRequiredFrameAsync(Stream stream)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var frame = await PacketFrameReader.ReadAsync(
            stream,
            PacketReadPolicy.TrustedServer,
            timeout.Token);
        return frame ?? throw new EndOfStreamException("Gateway test connection closed.");
    }

    private static async Task WriteBackendRouteEnvelopeAsync(
        Stream stream,
        PacketKind outerKind,
        GatewayBackendRouteEnvelope envelope)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var frame = PacketCodec.Encode(
            outerKind,
            Pid.GATE_BACKEND_ROUTE,
            GatewayBackendRouteEnvelope.ProtocolVersion,
            envelope,
            GatewayBackendRouteEnvelope.Codec);
        await PacketFrameWriter.WriteAsync(stream, frame, timeout.Token);
    }

    private static X509Certificate2 CreateServerCertificate()
    {
        var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=localhost",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
            false));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection
            {
                new Oid("1.3.6.1.5.5.7.3.1")
            },
            false));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        var subjectAlternativeNames = new SubjectAlternativeNameBuilder();
        subjectAlternativeNames.AddDnsName("localhost");
        request.CertificateExtensions.Add(subjectAlternativeNames.Build());

        var notBefore = DateTimeOffset.UtcNow.AddMinutes(-1);
        var notAfter = DateTimeOffset.UtcNow.AddHours(1);
        var serialNumber = RandomNumberGenerator.GetBytes(16);
        var generator = X509SignatureGenerator.CreateForRSA(rsa, RSASignaturePadding.Pkcs1);
        var certificate = request.Create(
            request.SubjectName,
            generator,
            notBefore,
            notAfter,
            serialNumber);
        return certificate.CopyWithPrivateKey(rsa);
    }

    private sealed class RecordingBackendRouteManager : IBackendRouteManager
    {
        private int m_RelayCount;

        public event BackendRouteFrameReceivedHandler? RouteFrameReceived;

        public TaskCompletionSource<ObservedBackendRouteFrame> RelayedFrame { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public int RelayCount => Volatile.Read(ref m_RelayCount);

        public string[] GetDiscoveredBackendKinds()
        {
            return ["alpha"];
        }

        public ValueTask<IBackendRouteSession> ConnectAsync(
            string backendKind,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException("ConnectionManager tests relay through RelayFrameAsync only.");
        }

        public ValueTask RelayFrameAsync(
            string backendKind,
            PacketFrame frame,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref m_RelayCount);
            RelayedFrame.TrySetResult(new ObservedBackendRouteFrame(
                backendKind,
                PacketCodec.Decode(frame, GatewayBackendRouteEnvelope.Codec)));
            return ValueTask.CompletedTask;
        }

        public async Task PublishBackendRouteFrameAsync(GatewayBackendRouteEnvelope envelope)
        {
            var frame = new BackendRouteFrameReceived(
                envelope.BackendKind,
                nodeId: "backend-a",
                masterConnectionId: "master-a",
                envelope);
            var handlers = RouteFrameReceived;
            if (handlers == null)
            {
                return;
            }

            foreach (BackendRouteFrameReceivedHandler handler in handlers.GetInvocationList())
            {
                await handler(frame, CancellationToken.None);
            }
        }
    }

    private sealed record ObservedBackendRouteFrame(
        string BackendKind,
        GatewayBackendRouteEnvelope Envelope);

    private sealed class StaticCertificateLoader(X509Certificate2 certificate) : IGatewayClientCertificateLoader
    {
        public bool? AllowUntrustedCertificate { get; private set; }

        public Task<X509Certificate2> LoadAsync(
            ConnectionManagerOptions options,
            bool allowUntrustedCertificate,
            CancellationToken cancellationToken)
        {
            AllowUntrustedCertificate = allowUntrustedCertificate;
            return Task.FromResult(certificate);
        }
    }

    private sealed class ThrowingCertificateLoader(Exception exception) : IGatewayClientCertificateLoader
    {
        public Task<X509Certificate2> LoadAsync(
            ConnectionManagerOptions options,
            bool allowUntrustedCertificate,
            CancellationToken cancellationToken)
        {
            return Task.FromException<X509Certificate2>(exception);
        }
    }

    private sealed class PassThroughStreamAuthenticator : IGatewayClientStreamAuthenticator
    {
        public ValueTask<Stream> AuthenticateAsync(
            NetworkStream networkStream,
            X509Certificate2 serverCertificate,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult<Stream>(networkStream);
        }
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;

        public string ApplicationName { get; set; } = "GatewayServer.Tests";

        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
