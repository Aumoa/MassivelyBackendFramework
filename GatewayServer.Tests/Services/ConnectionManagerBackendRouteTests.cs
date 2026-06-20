using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using GatewayServer.Behaviors;
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
    public async Task ClientRouteRequest_RejectsBeforeAuthentication()
    {
        var routeManager = new RecordingBackendRouteManager();
        var connectionManager = CreateConnectionManager(
            routeManager,
            new BackendRouteOptions
            {
                AllowedBackendKinds = ["alpha"],
                RequestTimeoutMilliseconds = 5000
            },
            out var port,
            authenticateClients: false);

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
            Assert.Equal("Client is not authenticated.", rejected.ErrorMessage);
            Assert.Equal(0, routeManager.RelayCount);
        }
        finally
        {
            await connectionManager.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task RouteOpenRequest_RejectsBeforeAuthentication()
    {
        var routeManager = new RecordingBackendRouteManager();
        var connectionManager = CreateConnectionManager(
            routeManager,
            new BackendRouteOptions
            {
                AllowedBackendKinds = ["alpha"],
                RequestTimeoutMilliseconds = 5000
            },
            out var port,
            authenticateClients: false);

        await connectionManager.StartAsync(CancellationToken.None);

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);
            await using var stream = client.GetStream();
            using (await ReadRequiredFrameAsync(stream))
            {
            }

            await WriteBackendRouteOpenRequestAsync(stream, new GatewayBackendRouteOpenRequest("alpha"));

            using var rejectedFrame = await ReadRequiredFrameAsync(stream);
            Assert.Equal(PacketKind.Response, rejectedFrame.Header.Kind);
            Assert.Equal(Pid.GATE_BACKEND_ROUTE_OPEN, rejectedFrame.Header.PacketId);

            var rejected = PacketCodec.Decode(rejectedFrame, GatewayBackendRouteOpenResponse.Codec);
            Assert.False(rejected.Success);
            Assert.Equal("alpha", rejected.BackendKind);
            Assert.Null(rejected.RouteToken);
            Assert.Equal("Client is not authenticated.", rejected.ErrorMessage);
            Assert.Equal(0, routeManager.RelayCount);
        }
        finally
        {
            await connectionManager.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task RouteOpenRequest_ReturnsGatewayIssuedTokenForAuthenticatedClient()
    {
        var routeManager = new RecordingBackendRouteManager();
        var connectionManager = CreateConnectionManager(
            routeManager,
            new BackendRouteOptions
            {
                AllowedBackendKinds = ["alpha"],
                RequestTimeoutMilliseconds = 5000,
                MaxOpenRoutes = 8,
                MaxOpenRoutesPerClient = 2,
                RouteLifetimeMilliseconds = 5000
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

            await WriteBackendRouteOpenRequestAsync(stream, new GatewayBackendRouteOpenRequest(" alpha "));

            using var acceptedFrame = await ReadRequiredFrameAsync(stream);
            Assert.Equal(PacketKind.Response, acceptedFrame.Header.Kind);
            Assert.Equal(Pid.GATE_BACKEND_ROUTE_OPEN, acceptedFrame.Header.PacketId);

            var accepted = PacketCodec.Decode(acceptedFrame, GatewayBackendRouteOpenResponse.Codec);
            Assert.True(accepted.Success);
            Assert.Equal("alpha", accepted.BackendKind);
            var routeToken = accepted.RouteToken;
            Assert.NotNull(routeToken);
            Assert.NotEqual("alpha", routeToken!.Value);
            Assert.True(routeToken.Value.Length >= 32);
            Assert.Equal(string.Empty, accepted.ErrorMessage);
            Assert.Equal(0, routeManager.RelayCount);

            Assert.Contains(connectionManager.GetStatusItems(), item =>
                item.Group == "Persistent Backend routes" &&
                item.Name == "Open routes" &&
                item.Value == "1");
            Assert.Contains(connectionManager.GetStatusItems(), item =>
                item.Group == "Persistent Backend route alpha" &&
                item.Name == "Open routes" &&
                item.Value == "1");
        }
        finally
        {
            await connectionManager.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task RouteOpenRequest_RejectsBackendKindWhenNotAllowlisted()
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

            await WriteBackendRouteOpenRequestAsync(stream, new GatewayBackendRouteOpenRequest("alpha"));

            using var rejectedFrame = await ReadRequiredFrameAsync(stream);
            Assert.Equal(PacketKind.Response, rejectedFrame.Header.Kind);
            Assert.Equal(Pid.GATE_BACKEND_ROUTE_OPEN, rejectedFrame.Header.PacketId);

            var rejected = PacketCodec.Decode(rejectedFrame, GatewayBackendRouteOpenResponse.Codec);
            Assert.False(rejected.Success);
            Assert.Equal("alpha", rejected.BackendKind);
            Assert.Null(rejected.RouteToken);
            Assert.Equal("Backend route open was rejected.", rejected.ErrorMessage);
            Assert.Equal(0, routeManager.RelayCount);

            Assert.Contains(connectionManager.GetStatusItems(), item =>
                item.Group == "Persistent Backend routes" &&
                item.Name == "Open routes" &&
                item.Value == "0");
        }
        finally
        {
            await connectionManager.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task ClientDisconnect_RemovesPersistentBackendRoutes()
    {
        var routeManager = new RecordingBackendRouteManager();
        var connectionManager = CreateConnectionManager(
            routeManager,
            new BackendRouteOptions
            {
                AllowedBackendKinds = ["alpha"],
                RequestTimeoutMilliseconds = 5000,
                RouteLifetimeMilliseconds = 5000
            },
            out var port);

        await connectionManager.StartAsync(CancellationToken.None);

        try
        {
            using (var client = new TcpClient())
            {
                await client.ConnectAsync(IPAddress.Loopback, port);
                await using var stream = client.GetStream();
                using (await ReadRequiredFrameAsync(stream))
                {
                }

                await WriteBackendRouteOpenRequestAsync(stream, new GatewayBackendRouteOpenRequest("alpha"));
                using var acceptedFrame = await ReadRequiredFrameAsync(stream);
                var accepted = PacketCodec.Decode(acceptedFrame, GatewayBackendRouteOpenResponse.Codec);
                Assert.True(accepted.Success);

                Assert.Contains(connectionManager.GetStatusItems(), item =>
                    item.Group == "Persistent Backend routes" &&
                    item.Name == "Open routes" &&
                    item.Value == "1");
            }

            await WaitForStatusValueAsync(
                connectionManager,
                "Persistent Backend routes",
                "Open routes",
                "0");
        }
        finally
        {
            await connectionManager.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task RouteDataNotify_RelaysToBackendForOwningRoute()
    {
        var routeManager = new RecordingBackendRouteManager();
        var connectionManager = CreateConnectionManager(
            routeManager,
            new BackendRouteOptions
            {
                AllowedBackendKinds = ["alpha"],
                RequestTimeoutMilliseconds = 5000,
                RouteLifetimeMilliseconds = 5000
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

            var routeToken = await OpenPersistentBackendRouteAsync(stream);
            byte[] payload = [1, 2, 3, 4];
            var dataEnvelope = new GatewayBackendRouteDataEnvelope(
                routeToken,
                GatewayBackendRouteDirection.ClientToBackend,
                PacketKind.Notify,
                routedPacketId: 301,
                routedVersion: 2,
                exchangeId: null,
                payload);

            await WriteBackendRouteDataEnvelopeAsync(stream, PacketKind.Notify, dataEnvelope);

            var relayed = await routeManager.RelayedDataFrame.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("alpha", relayed.BackendKind);
            Assert.Equal(routeToken, relayed.Envelope.RouteToken);
            Assert.Equal(GatewayBackendRouteDirection.ClientToBackend, relayed.Envelope.Direction);
            Assert.Equal(PacketKind.Notify, relayed.Envelope.RoutedKind);
            Assert.Equal((ushort)301, relayed.Envelope.RoutedPacketId);
            Assert.Equal((ushort)2, relayed.Envelope.RoutedVersion);
            Assert.False(relayed.Envelope.ExchangeId.HasValue);
            Assert.Equal(payload, relayed.Envelope.RoutedPayload);
            Assert.Equal(1, routeManager.RelayCount);
        }
        finally
        {
            await connectionManager.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task RouteDataRequest_RegistersClientOriginExchangeAndRelays()
    {
        var routeManager = new RecordingBackendRouteManager();
        var connectionManager = CreateConnectionManager(
            routeManager,
            new BackendRouteOptions
            {
                AllowedBackendKinds = ["alpha"],
                RequestTimeoutMilliseconds = 5000,
                RouteLifetimeMilliseconds = 5000
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

            var routeToken = await OpenPersistentBackendRouteAsync(stream);
            var exchangeId = new GatewayBackendExchangeId(Guid.NewGuid());
            byte[] payload = [9, 8, 7];
            var dataEnvelope = new GatewayBackendRouteDataEnvelope(
                routeToken,
                GatewayBackendRouteDirection.ClientToBackend,
                PacketKind.Request,
                routedPacketId: 401,
                routedVersion: 3,
                exchangeId,
                payload);

            await WriteBackendRouteDataEnvelopeAsync(stream, PacketKind.Request, dataEnvelope);

            var relayed = await routeManager.RelayedDataFrame.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("alpha", relayed.BackendKind);
            Assert.Equal(routeToken, relayed.Envelope.RouteToken);
            Assert.Equal(PacketKind.Request, relayed.Envelope.RoutedKind);
            Assert.True(relayed.Envelope.ExchangeId.HasValue);
            Assert.Equal(exchangeId, relayed.Envelope.ExchangeId.Value);
            Assert.Equal(payload, relayed.Envelope.RoutedPayload);
            Assert.Contains(connectionManager.GetStatusItems(), item =>
                item.Group == "Persistent Backend routes" &&
                item.Name == "Pending client exchanges" &&
                item.Value == "1");
            Assert.Contains(connectionManager.GetStatusItems(), item =>
                item.Group == "Persistent Backend route alpha" &&
                item.Name == "Pending client exchanges" &&
                item.Value == "1");
        }
        finally
        {
            await connectionManager.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task RouteDataRequest_RejectsDuplicateExchangeIdWithoutRemovingExistingExchange()
    {
        var routeManager = new RecordingBackendRouteManager();
        var connectionManager = CreateConnectionManager(
            routeManager,
            new BackendRouteOptions
            {
                AllowedBackendKinds = ["alpha"],
                RequestTimeoutMilliseconds = 5000,
                RouteLifetimeMilliseconds = 5000
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

            var routeToken = await OpenPersistentBackendRouteAsync(stream);
            var exchangeId = new GatewayBackendExchangeId(Guid.NewGuid());
            var firstEnvelope = new GatewayBackendRouteDataEnvelope(
                routeToken,
                GatewayBackendRouteDirection.ClientToBackend,
                PacketKind.Request,
                routedPacketId: 401,
                routedVersion: 3,
                exchangeId,
                [1]);
            var duplicateEnvelope = new GatewayBackendRouteDataEnvelope(
                routeToken,
                GatewayBackendRouteDirection.ClientToBackend,
                PacketKind.Request,
                routedPacketId: 402,
                routedVersion: 3,
                exchangeId,
                [2]);

            await WriteBackendRouteDataEnvelopeAsync(stream, PacketKind.Request, firstEnvelope);
            await routeManager.RelayedDataFrame.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, routeManager.RelayCount);

            await WriteBackendRouteDataEnvelopeAsync(stream, PacketKind.Request, duplicateEnvelope);
            await Task.Delay(TimeSpan.FromMilliseconds(250));

            Assert.Equal(1, routeManager.RelayCount);
            Assert.Contains(connectionManager.GetStatusItems(), item =>
                item.Group == "Persistent Backend routes" &&
                item.Name == "Pending client exchanges" &&
                item.Value == "1");
        }
        finally
        {
            await connectionManager.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task BackendRouteDataNotify_ForwardsToOwningClient()
    {
        var routeManager = new RecordingBackendRouteManager();
        var connectionManager = CreateConnectionManager(
            routeManager,
            new BackendRouteOptions
            {
                AllowedBackendKinds = ["alpha"],
                RequestTimeoutMilliseconds = 5000,
                RouteLifetimeMilliseconds = 5000
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

            var routeToken = await OpenPersistentBackendRouteAsync(stream);
            byte[] payload = [7, 7, 7];
            var envelope = new GatewayBackendRouteDataEnvelope(
                routeToken,
                GatewayBackendRouteDirection.BackendToClient,
                PacketKind.Notify,
                routedPacketId: 501,
                routedVersion: 2,
                exchangeId: null,
                payload);

            await routeManager.PublishBackendRouteDataFrameAsync("alpha", envelope);

            using var frame = await ReadRequiredFrameAsync(stream);
            Assert.Equal(PacketKind.Notify, frame.Header.Kind);
            Assert.Equal(Pid.GATE_BACKEND_ROUTE_DATA, frame.Header.PacketId);

            var forwarded = PacketCodec.Decode(frame, GatewayBackendRouteDataEnvelope.Codec);
            Assert.Equal(routeToken, forwarded.RouteToken);
            Assert.Equal(GatewayBackendRouteDirection.BackendToClient, forwarded.Direction);
            Assert.Equal(PacketKind.Notify, forwarded.RoutedKind);
            Assert.Equal((ushort)501, forwarded.RoutedPacketId);
            Assert.Equal((ushort)2, forwarded.RoutedVersion);
            Assert.False(forwarded.ExchangeId.HasValue);
            Assert.Equal(payload, forwarded.RoutedPayload);
        }
        finally
        {
            await connectionManager.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task BackendRouteDataResponse_MatchesClientOriginExchangeAndForwards()
    {
        var routeManager = new RecordingBackendRouteManager();
        var connectionManager = CreateConnectionManager(
            routeManager,
            new BackendRouteOptions
            {
                AllowedBackendKinds = ["alpha"],
                RequestTimeoutMilliseconds = 5000,
                RouteLifetimeMilliseconds = 5000
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

            var routeToken = await OpenPersistentBackendRouteAsync(stream);
            var exchangeId = new GatewayBackendExchangeId(Guid.NewGuid());
            var requestEnvelope = new GatewayBackendRouteDataEnvelope(
                routeToken,
                GatewayBackendRouteDirection.ClientToBackend,
                PacketKind.Request,
                routedPacketId: 401,
                routedVersion: 3,
                exchangeId,
                [1, 2, 3]);
            await WriteBackendRouteDataEnvelopeAsync(stream, PacketKind.Request, requestEnvelope);
            await routeManager.RelayedDataFrame.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Contains(connectionManager.GetStatusItems(), item =>
                item.Group == "Persistent Backend routes" &&
                item.Name == "Pending client exchanges" &&
                item.Value == "1");

            byte[] responsePayload = [4, 5, 6];
            var responseEnvelope = new GatewayBackendRouteDataEnvelope(
                routeToken,
                GatewayBackendRouteDirection.BackendToClient,
                PacketKind.Response,
                routedPacketId: 402,
                routedVersion: 3,
                exchangeId,
                responsePayload);
            await routeManager.PublishBackendRouteDataFrameAsync("alpha", responseEnvelope);

            using var responseFrame = await ReadRequiredFrameAsync(stream);
            Assert.Equal(PacketKind.Response, responseFrame.Header.Kind);
            Assert.Equal(Pid.GATE_BACKEND_ROUTE_DATA, responseFrame.Header.PacketId);

            var forwarded = PacketCodec.Decode(responseFrame, GatewayBackendRouteDataEnvelope.Codec);
            Assert.Equal(routeToken, forwarded.RouteToken);
            Assert.Equal(GatewayBackendRouteDirection.BackendToClient, forwarded.Direction);
            Assert.Equal(PacketKind.Response, forwarded.RoutedKind);
            Assert.True(forwarded.ExchangeId.HasValue);
            Assert.Equal(exchangeId, forwarded.ExchangeId.Value);
            Assert.Equal(responsePayload, forwarded.RoutedPayload);
            Assert.Contains(connectionManager.GetStatusItems(), item =>
                item.Group == "Persistent Backend routes" &&
                item.Name == "Pending client exchanges" &&
                item.Value == "0");
            Assert.Contains(connectionManager.GetStatusItems(), item =>
                item.Group == "Persistent Backend routes" &&
                item.Name == "Open routes" &&
                item.Value == "1");
        }
        finally
        {
            await connectionManager.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task BackendRouteDataRequest_RegistersBackendOriginExchangeAndClientResponseRelays()
    {
        var routeManager = new RecordingBackendRouteManager();
        var connectionManager = CreateConnectionManager(
            routeManager,
            new BackendRouteOptions
            {
                AllowedBackendKinds = ["alpha"],
                RequestTimeoutMilliseconds = 5000,
                RouteLifetimeMilliseconds = 5000
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

            var routeToken = await OpenPersistentBackendRouteAsync(stream);
            var exchangeId = new GatewayBackendExchangeId(Guid.NewGuid());
            byte[] requestPayload = [8, 8, 8];
            var backendRequest = new GatewayBackendRouteDataEnvelope(
                routeToken,
                GatewayBackendRouteDirection.BackendToClient,
                PacketKind.Request,
                routedPacketId: 601,
                routedVersion: 4,
                exchangeId,
                requestPayload);

            await routeManager.PublishBackendRouteDataFrameAsync("alpha", backendRequest);

            using (var requestFrame = await ReadRequiredFrameAsync(stream))
            {
                Assert.Equal(PacketKind.Request, requestFrame.Header.Kind);
                Assert.Equal(Pid.GATE_BACKEND_ROUTE_DATA, requestFrame.Header.PacketId);

                var forwarded = PacketCodec.Decode(requestFrame, GatewayBackendRouteDataEnvelope.Codec);
                Assert.Equal(routeToken, forwarded.RouteToken);
                Assert.Equal(GatewayBackendRouteDirection.BackendToClient, forwarded.Direction);
                Assert.Equal(PacketKind.Request, forwarded.RoutedKind);
                Assert.True(forwarded.ExchangeId.HasValue);
                Assert.Equal(exchangeId, forwarded.ExchangeId.Value);
                Assert.Equal(requestPayload, forwarded.RoutedPayload);
            }

            Assert.Contains(connectionManager.GetStatusItems(), item =>
                item.Group == "Persistent Backend routes" &&
                item.Name == "Pending backend exchanges" &&
                item.Value == "1");

            byte[] responsePayload = [9, 9];
            var clientResponse = new GatewayBackendRouteDataEnvelope(
                routeToken,
                GatewayBackendRouteDirection.ClientToBackend,
                PacketKind.Response,
                routedPacketId: 602,
                routedVersion: 4,
                exchangeId,
                responsePayload);
            await WriteBackendRouteDataEnvelopeAsync(stream, PacketKind.Response, clientResponse);

            var relayed = await routeManager.RelayedDataFrame.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("alpha", relayed.BackendKind);
            Assert.Equal(routeToken, relayed.Envelope.RouteToken);
            Assert.Equal(GatewayBackendRouteDirection.ClientToBackend, relayed.Envelope.Direction);
            Assert.Equal(PacketKind.Response, relayed.Envelope.RoutedKind);
            Assert.True(relayed.Envelope.ExchangeId.HasValue);
            Assert.Equal(exchangeId, relayed.Envelope.ExchangeId.Value);
            Assert.Equal(responsePayload, relayed.Envelope.RoutedPayload);
            await WaitForStatusValueAsync(
                connectionManager,
                "Persistent Backend routes",
                "Pending backend exchanges",
                "0");
        }
        finally
        {
            await connectionManager.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task BackendRouteDataResponse_RejectsUnknownClientOriginExchange()
    {
        var routeManager = new RecordingBackendRouteManager();
        var connectionManager = CreateConnectionManager(
            routeManager,
            new BackendRouteOptions
            {
                AllowedBackendKinds = ["alpha"],
                RequestTimeoutMilliseconds = 5000,
                RouteLifetimeMilliseconds = 5000
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

            var routeToken = await OpenPersistentBackendRouteAsync(stream);
            var responseEnvelope = new GatewayBackendRouteDataEnvelope(
                routeToken,
                GatewayBackendRouteDirection.BackendToClient,
                PacketKind.Response,
                routedPacketId: 402,
                routedVersion: 3,
                new GatewayBackendExchangeId(Guid.NewGuid()),
                [4, 5, 6]);

            await routeManager.PublishBackendRouteDataFrameAsync("alpha", responseEnvelope);

            await AssertNoClientFrameAsync(stream);
            Assert.Contains(connectionManager.GetStatusItems(), item =>
                item.Group == "Persistent Backend routes" &&
                item.Name == "Pending client exchanges" &&
                item.Value == "0");
        }
        finally
        {
            await connectionManager.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task ClientRouteDataResponse_RejectsUnknownBackendOriginExchange()
    {
        var routeManager = new RecordingBackendRouteManager();
        var connectionManager = CreateConnectionManager(
            routeManager,
            new BackendRouteOptions
            {
                AllowedBackendKinds = ["alpha"],
                RequestTimeoutMilliseconds = 5000,
                RouteLifetimeMilliseconds = 5000
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

            var routeToken = await OpenPersistentBackendRouteAsync(stream);
            var responseEnvelope = new GatewayBackendRouteDataEnvelope(
                routeToken,
                GatewayBackendRouteDirection.ClientToBackend,
                PacketKind.Response,
                routedPacketId: 602,
                routedVersion: 4,
                new GatewayBackendExchangeId(Guid.NewGuid()),
                [9, 9]);
            await WriteBackendRouteDataEnvelopeAsync(stream, PacketKind.Response, responseEnvelope);

            await AssertNoDataRelayAsync(routeManager);
            Assert.Contains(connectionManager.GetStatusItems(), item =>
                item.Group == "Persistent Backend routes" &&
                item.Name == "Pending backend exchanges" &&
                item.Value == "0");
        }
        finally
        {
            await connectionManager.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task BackendRouteData_RejectsBackendKindMismatch()
    {
        var routeManager = new RecordingBackendRouteManager();
        var connectionManager = CreateConnectionManager(
            routeManager,
            new BackendRouteOptions
            {
                AllowedBackendKinds = ["alpha"],
                RequestTimeoutMilliseconds = 5000,
                RouteLifetimeMilliseconds = 5000
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

            var routeToken = await OpenPersistentBackendRouteAsync(stream);
            var envelope = new GatewayBackendRouteDataEnvelope(
                routeToken,
                GatewayBackendRouteDirection.BackendToClient,
                PacketKind.Notify,
                routedPacketId: 501,
                routedVersion: 2,
                exchangeId: null,
                [7, 7, 7]);

            await routeManager.PublishBackendRouteDataFrameAsync("beta", envelope);

            await AssertNoClientFrameAsync(stream);
        }
        finally
        {
            await connectionManager.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task RouteCloseRequest_ClosesRouteAndAcknowledges()
    {
        var routeManager = new RecordingBackendRouteManager();
        var connectionManager = CreateConnectionManager(
            routeManager,
            new BackendRouteOptions
            {
                AllowedBackendKinds = ["alpha"],
                RequestTimeoutMilliseconds = 5000,
                RouteLifetimeMilliseconds = 5000
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

            var routeToken = await OpenPersistentBackendRouteAsync(stream);
            var close = new GatewayBackendRouteClose(routeToken, "client closed");
            await WriteBackendRouteCloseRequestAsync(stream, close);

            using (var closeFrame = await ReadRequiredFrameAsync(stream))
            {
                Assert.Equal(PacketKind.Response, closeFrame.Header.Kind);
                Assert.Equal(Pid.GATE_BACKEND_ROUTE_CLOSE, closeFrame.Header.PacketId);

                var acknowledged = PacketCodec.Decode(closeFrame, GatewayBackendRouteClose.Codec);
                Assert.Equal(routeToken, acknowledged.RouteToken);
                Assert.Equal("client closed", acknowledged.Reason);
            }

            await WaitForStatusValueAsync(
                connectionManager,
                "Persistent Backend routes",
                "Open routes",
                "0");

            var dataEnvelope = new GatewayBackendRouteDataEnvelope(
                routeToken,
                GatewayBackendRouteDirection.ClientToBackend,
                PacketKind.Notify,
                routedPacketId: 301,
                routedVersion: 2,
                exchangeId: null,
                [1, 2, 3]);
            await WriteBackendRouteDataEnvelopeAsync(stream, PacketKind.Notify, dataEnvelope);

            await AssertNoDataRelayAsync(routeManager);
        }
        finally
        {
            await connectionManager.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task RouteCloseRequest_RejectsRouteTokenOwnedByAnotherClient()
    {
        var routeManager = new RecordingBackendRouteManager();
        var connectionManager = CreateConnectionManager(
            routeManager,
            new BackendRouteOptions
            {
                AllowedBackendKinds = ["alpha"],
                RequestTimeoutMilliseconds = 5000,
                RouteLifetimeMilliseconds = 5000
            },
            out var port);

        await connectionManager.StartAsync(CancellationToken.None);

        try
        {
            using var ownerClient = new TcpClient();
            await ownerClient.ConnectAsync(IPAddress.Loopback, port);
            await using var ownerStream = ownerClient.GetStream();
            using (await ReadRequiredFrameAsync(ownerStream))
            {
            }

            var routeToken = await OpenPersistentBackendRouteAsync(ownerStream);

            using var otherClient = new TcpClient();
            await otherClient.ConnectAsync(IPAddress.Loopback, port);
            await using var otherStream = otherClient.GetStream();
            using (await ReadRequiredFrameAsync(otherStream))
            {
            }

            await WriteBackendRouteCloseRequestAsync(
                otherStream,
                new GatewayBackendRouteClose(routeToken, "other client"));

            await AssertNoClientFrameAsync(otherStream);
            Assert.Contains(connectionManager.GetStatusItems(), item =>
                item.Group == "Persistent Backend routes" &&
                item.Name == "Open routes" &&
                item.Value == "1");

            var dataEnvelope = new GatewayBackendRouteDataEnvelope(
                routeToken,
                GatewayBackendRouteDirection.ClientToBackend,
                PacketKind.Notify,
                routedPacketId: 301,
                routedVersion: 2,
                exchangeId: null,
                [1, 2, 3]);
            await WriteBackendRouteDataEnvelopeAsync(ownerStream, PacketKind.Notify, dataEnvelope);

            var relayed = await routeManager.RelayedDataFrame.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(routeToken, relayed.Envelope.RouteToken);
            Assert.Equal(PacketKind.Notify, relayed.Envelope.RoutedKind);
        }
        finally
        {
            await connectionManager.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task RouteCloseRequest_ClearsPendingExchanges()
    {
        var routeManager = new RecordingBackendRouteManager();
        var connectionManager = CreateConnectionManager(
            routeManager,
            new BackendRouteOptions
            {
                AllowedBackendKinds = ["alpha"],
                RequestTimeoutMilliseconds = 5000,
                RouteLifetimeMilliseconds = 5000
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

            var routeToken = await OpenPersistentBackendRouteAsync(stream);
            var clientExchangeId = new GatewayBackendExchangeId(Guid.NewGuid());
            var clientRequest = new GatewayBackendRouteDataEnvelope(
                routeToken,
                GatewayBackendRouteDirection.ClientToBackend,
                PacketKind.Request,
                routedPacketId: 401,
                routedVersion: 3,
                clientExchangeId,
                [1, 2, 3]);
            await WriteBackendRouteDataEnvelopeAsync(stream, PacketKind.Request, clientRequest);
            await routeManager.RelayedDataFrame.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var backendExchangeId = new GatewayBackendExchangeId(Guid.NewGuid());
            var backendRequest = new GatewayBackendRouteDataEnvelope(
                routeToken,
                GatewayBackendRouteDirection.BackendToClient,
                PacketKind.Request,
                routedPacketId: 601,
                routedVersion: 4,
                backendExchangeId,
                [8, 8, 8]);
            await routeManager.PublishBackendRouteDataFrameAsync("alpha", backendRequest);
            using (await ReadRequiredFrameAsync(stream))
            {
            }

            Assert.Contains(connectionManager.GetStatusItems(), item =>
                item.Group == "Persistent Backend routes" &&
                item.Name == "Pending client exchanges" &&
                item.Value == "1");
            Assert.Contains(connectionManager.GetStatusItems(), item =>
                item.Group == "Persistent Backend routes" &&
                item.Name == "Pending backend exchanges" &&
                item.Value == "1");

            await WriteBackendRouteCloseRequestAsync(
                stream,
                new GatewayBackendRouteClose(routeToken, "client closed"));
            using (await ReadRequiredFrameAsync(stream))
            {
            }

            await WaitForStatusValueAsync(
                connectionManager,
                "Persistent Backend routes",
                "Open routes",
                "0");
            Assert.Contains(connectionManager.GetStatusItems(), item =>
                item.Group == "Persistent Backend routes" &&
                item.Name == "Pending client exchanges" &&
                item.Value == "0");
            Assert.Contains(connectionManager.GetStatusItems(), item =>
                item.Group == "Persistent Backend routes" &&
                item.Name == "Pending backend exchanges" &&
                item.Value == "0");
        }
        finally
        {
            await connectionManager.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task RouteData_RejectsUnknownRouteToken()
    {
        var routeManager = new RecordingBackendRouteManager();
        var connectionManager = CreateConnectionManager(
            routeManager,
            new BackendRouteOptions
            {
                AllowedBackendKinds = ["alpha"],
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

            var dataEnvelope = new GatewayBackendRouteDataEnvelope(
                new GatewayBackendRouteToken("unknown-route-token"),
                GatewayBackendRouteDirection.ClientToBackend,
                PacketKind.Notify,
                routedPacketId: 301,
                routedVersion: 2,
                exchangeId: null,
                [1, 2, 3]);

            await WriteBackendRouteDataEnvelopeAsync(stream, PacketKind.Notify, dataEnvelope);

            await AssertNoDataRelayAsync(routeManager);
        }
        finally
        {
            await connectionManager.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task RouteData_RejectsRouteTokenOwnedByAnotherClient()
    {
        var routeManager = new RecordingBackendRouteManager();
        var connectionManager = CreateConnectionManager(
            routeManager,
            new BackendRouteOptions
            {
                AllowedBackendKinds = ["alpha"],
                RequestTimeoutMilliseconds = 5000,
                RouteLifetimeMilliseconds = 5000
            },
            out var port);

        await connectionManager.StartAsync(CancellationToken.None);

        try
        {
            using var ownerClient = new TcpClient();
            await ownerClient.ConnectAsync(IPAddress.Loopback, port);
            await using var ownerStream = ownerClient.GetStream();
            using (await ReadRequiredFrameAsync(ownerStream))
            {
            }

            var routeToken = await OpenPersistentBackendRouteAsync(ownerStream);

            using var otherClient = new TcpClient();
            await otherClient.ConnectAsync(IPAddress.Loopback, port);
            await using var otherStream = otherClient.GetStream();
            using (await ReadRequiredFrameAsync(otherStream))
            {
            }

            var dataEnvelope = new GatewayBackendRouteDataEnvelope(
                routeToken,
                GatewayBackendRouteDirection.ClientToBackend,
                PacketKind.Notify,
                routedPacketId: 301,
                routedVersion: 2,
                exchangeId: null,
                [1, 2, 3]);

            await WriteBackendRouteDataEnvelopeAsync(otherStream, PacketKind.Notify, dataEnvelope);

            await AssertNoDataRelayAsync(routeManager);
        }
        finally
        {
            await connectionManager.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task RouteData_RejectsBackendToClientDirectionFromClient()
    {
        var routeManager = new RecordingBackendRouteManager();
        var connectionManager = CreateConnectionManager(
            routeManager,
            new BackendRouteOptions
            {
                AllowedBackendKinds = ["alpha"],
                RequestTimeoutMilliseconds = 5000,
                RouteLifetimeMilliseconds = 5000
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

            var routeToken = await OpenPersistentBackendRouteAsync(stream);
            var dataEnvelope = new GatewayBackendRouteDataEnvelope(
                routeToken,
                GatewayBackendRouteDirection.BackendToClient,
                PacketKind.Notify,
                routedPacketId: 301,
                routedVersion: 2,
                exchangeId: null,
                [1, 2, 3]);

            await WriteBackendRouteDataEnvelopeAsync(stream, PacketKind.Notify, dataEnvelope);

            await AssertNoDataRelayAsync(routeManager);
        }
        finally
        {
            await connectionManager.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task UnsupportedClientResponse_IsNotEchoed()
    {
        var routeManager = new RecordingBackendRouteManager();
        var connectionManager = CreateConnectionManager(
            routeManager,
            new BackendRouteOptions
            {
                AllowedBackendKinds = ["alpha"],
                RequestTimeoutMilliseconds = 5000,
                RouteLifetimeMilliseconds = 5000
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

            using var response = PacketFrame.Create(
                PacketKind.Response,
                packetId: 999,
                version: 1,
                new byte[] { 1, 2, 3 });
            await PacketFrameWriter.WriteAsync(stream, response, CancellationToken.None);

            await AssertNoClientFrameAsync(stream);
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
        IGatewayClientStreamAuthenticator? streamAuthenticator = null,
        bool authenticateClients = true)
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
            new StaticGatewayClientAuthenticationContextFactory(authenticateClients),
            new GatewayBackendRouteTokenGenerator(),
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

    private static async Task WriteBackendRouteOpenRequestAsync(
        Stream stream,
        GatewayBackendRouteOpenRequest request)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var frame = PacketCodec.Encode(
            PacketKind.Request,
            Pid.GATE_BACKEND_ROUTE_OPEN,
            GatewayBackendRouteOpenRequest.ProtocolVersion,
            request,
            GatewayBackendRouteOpenRequest.Codec);
        await PacketFrameWriter.WriteAsync(stream, frame, timeout.Token);
    }

    private static async Task<GatewayBackendRouteToken> OpenPersistentBackendRouteAsync(
        Stream stream,
        string backendKind = "alpha")
    {
        await WriteBackendRouteOpenRequestAsync(stream, new GatewayBackendRouteOpenRequest(backendKind));
        using var acceptedFrame = await ReadRequiredFrameAsync(stream);
        Assert.Equal(PacketKind.Response, acceptedFrame.Header.Kind);
        Assert.Equal(Pid.GATE_BACKEND_ROUTE_OPEN, acceptedFrame.Header.PacketId);

        var accepted = PacketCodec.Decode(acceptedFrame, GatewayBackendRouteOpenResponse.Codec);
        Assert.True(accepted.Success);
        Assert.NotNull(accepted.RouteToken);
        return accepted.RouteToken!;
    }

    private static async Task WriteBackendRouteCloseRequestAsync(
        Stream stream,
        GatewayBackendRouteClose close)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var frame = PacketCodec.Encode(
            PacketKind.Request,
            Pid.GATE_BACKEND_ROUTE_CLOSE,
            GatewayBackendRouteClose.ProtocolVersion,
            close,
            GatewayBackendRouteClose.Codec);
        await PacketFrameWriter.WriteAsync(stream, frame, timeout.Token);
    }

    private static async Task WriteBackendRouteDataEnvelopeAsync(
        Stream stream,
        PacketKind outerKind,
        GatewayBackendRouteDataEnvelope envelope)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var frame = PacketCodec.Encode(
            outerKind,
            Pid.GATE_BACKEND_ROUTE_DATA,
            GatewayBackendRouteDataEnvelope.ProtocolVersion,
            envelope,
            GatewayBackendRouteDataEnvelope.Codec);
        await PacketFrameWriter.WriteAsync(stream, frame, timeout.Token);
    }

    private static async Task AssertNoDataRelayAsync(RecordingBackendRouteManager routeManager)
    {
        var completed = await Task.WhenAny(
            routeManager.RelayedDataFrame.Task,
            Task.Delay(TimeSpan.FromMilliseconds(250)));
        Assert.NotSame(routeManager.RelayedDataFrame.Task, completed);
        Assert.Equal(0, routeManager.RelayCount);
    }

    private static async Task AssertNoClientFrameAsync(Stream stream)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
        try
        {
            using var frame = await PacketFrameReader.ReadAsync(
                stream,
                PacketReadPolicy.TrustedServer,
                timeout.Token);
            Assert.Null(frame);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
        }
    }

    private static async Task WaitForStatusValueAsync(
        ConnectionManager connectionManager,
        string group,
        string name,
        string value)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (connectionManager.GetStatusItems().Any(item =>
                    item.Group == group &&
                    item.Name == name &&
                    item.Value == value))
            {
                return;
            }

            await Task.Delay(25);
        }

        Assert.Contains(connectionManager.GetStatusItems(), item =>
            item.Group == group &&
            item.Name == name &&
            item.Value == value);
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

        public event BackendRouteDataFrameReceivedHandler? RouteDataFrameReceived;

        public TaskCompletionSource<ObservedBackendRouteFrame> RelayedFrame { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<ObservedBackendRouteDataFrame> RelayedDataFrame { get; } = new(
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
            if (frame.Header.PacketId == Pid.GATE_BACKEND_ROUTE)
            {
                RelayedFrame.TrySetResult(new ObservedBackendRouteFrame(
                    backendKind,
                    PacketCodec.Decode(frame, GatewayBackendRouteEnvelope.Codec)));
            }
            else if (frame.Header.PacketId == Pid.GATE_BACKEND_ROUTE_DATA)
            {
                RelayedDataFrame.TrySetResult(new ObservedBackendRouteDataFrame(
                    backendKind,
                    PacketCodec.Decode(frame, GatewayBackendRouteDataEnvelope.Codec)));
            }
            else
            {
                throw new InvalidOperationException("ConnectionManager relayed an unexpected Backend route packet.");
            }

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

        public async Task PublishBackendRouteDataFrameAsync(
            string backendKind,
            GatewayBackendRouteDataEnvelope envelope)
        {
            var frame = new BackendRouteDataFrameReceived(
                backendKind,
                nodeId: "backend-a",
                masterConnectionId: "master-a",
                envelope);
            var handlers = RouteDataFrameReceived;
            if (handlers == null)
            {
                return;
            }

            foreach (BackendRouteDataFrameReceivedHandler handler in handlers.GetInvocationList())
            {
                await handler(frame, CancellationToken.None);
            }
        }
    }

    private sealed record ObservedBackendRouteFrame(
        string BackendKind,
        GatewayBackendRouteEnvelope Envelope);

    private sealed record ObservedBackendRouteDataFrame(
        string BackendKind,
        GatewayBackendRouteDataEnvelope Envelope);

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

    private sealed class StaticGatewayClientAuthenticationContextFactory(bool authenticateClients)
        : IGatewayClientAuthenticationContextFactory
    {
        public GatewayClientAuthenticationContext Create(Client client)
        {
            var context = new GatewayClientAuthenticationContext();
            if (authenticateClients)
            {
                context.MarkAuthenticated(new GatewayClientPrincipal("test-client"));
            }

            return context;
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
