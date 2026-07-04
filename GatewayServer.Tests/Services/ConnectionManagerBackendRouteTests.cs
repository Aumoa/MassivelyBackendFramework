using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using GatewayServer.Behaviors;
using GatewayServer.Options;
using GatewayServer.Protocols;
using GatewayServer.Services;
using MasterServer.ControlPlane;
using Microsoft.Extensions.Logging.Abstractions;
using PacketCore;
using Xunit;

namespace GatewayServer.Tests.Services;

public sealed class ConnectionManagerBackendRouteTests
{
    [Fact]
    public async Task ClientRouteRequest_RejectsRemovedLegacyOneShotRoutes()
    {
        var routeManager = new RecordingBackendRouteManager();
        var connectionManager = CreateConnectionManager(
            routeManager,
            new BackendRouteOptions
            {
                RequestTimeoutMilliseconds = 5000
            },
            out var port,
            allowedBackendKinds: []);

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
            Assert.Equal("Legacy Backend route flow has been removed.", rejected.ErrorMessage);
            Assert.Equal(0, routeManager.RelayCount);
            Assert.Equal(0, routeManager.ConnectCount);
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
    public async Task RouteOpenRequest_RejectsLegacyBeforeAuthenticationWithLegacyResponse()
    {
        var routeManager = new RecordingBackendRouteManager();
        var connectionManager = CreateConnectionManager(
            routeManager,
            new BackendRouteOptions
            {
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

            await WriteBackendRouteOpenRequestAsync(
                stream,
                new GatewayBackendRouteOpenRequest("alpha"),
                GatewayBackendRouteOpenRequest.LegacyProtocolVersion,
                GatewayBackendRouteOpenRequest.LegacyCodec);

            using var rejectedFrame = await ReadRequiredFrameAsync(stream);
            Assert.Equal(PacketKind.Response, rejectedFrame.Header.Kind);
            Assert.Equal(Pid.GATE_BACKEND_ROUTE_OPEN, rejectedFrame.Header.PacketId);
            Assert.Equal(GatewayBackendRouteOpenResponse.LegacyProtocolVersion, rejectedFrame.Header.Version);

            var rejected = PacketCodec.Decode(rejectedFrame, GatewayBackendRouteOpenResponse.LegacyCodec);
            Assert.False(rejected.Success);
            Assert.Equal("alpha", rejected.BackendKind);
            Assert.Null(rejected.RouteToken);
            Assert.Equal("Client is not authenticated.", rejected.ErrorMessage);
            Assert.Null(rejected.ServerHandle);
            Assert.Equal(string.Empty, rejected.DescriptorVersion);
            Assert.Equal(string.Empty, rejected.DescriptorHash);
            Assert.Equal(0, routeManager.RelayCount);
        }
        finally
        {
            await connectionManager.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task ServerListRequest_RejectsBeforeAuthentication()
    {
        var routeManager = new RecordingBackendRouteManager();
        var connectionManager = CreateConnectionManager(
            routeManager,
            new BackendRouteOptions
            {
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

            await WriteBackendServerListRequestAsync(stream, new GatewayBackendServerListRequest("alpha"));

            using var rejectedFrame = await ReadRequiredFrameAsync(stream);
            Assert.Equal(PacketKind.Response, rejectedFrame.Header.Kind);
            Assert.Equal(Pid.GATE_BACKEND_SERVER_LIST, rejectedFrame.Header.PacketId);

            var rejected = PacketCodec.Decode(rejectedFrame, GatewayBackendServerListResponse.Codec);
            Assert.False(rejected.Success);
            Assert.Equal("alpha", rejected.BackendKind);
            Assert.Empty(rejected.Entries);
            Assert.Equal("Client is not authenticated.", rejected.ErrorMessage);
            Assert.Equal(0, routeManager.ConnectCount);
        }
        finally
        {
            await connectionManager.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task ServerListRequest_ReturnsClientSafeDirectoryForAuthenticatedClient()
    {
        var routeManager = new RecordingBackendRouteManager();
        var connectionManager = CreateConnectionManager(
            routeManager,
            new BackendRouteOptions
            {
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

            await WriteBackendServerListRequestAsync(stream, new GatewayBackendServerListRequest(" alpha "));

            using var responseFrame = await ReadRequiredFrameAsync(stream);
            Assert.Equal(PacketKind.Response, responseFrame.Header.Kind);
            Assert.Equal(Pid.GATE_BACKEND_SERVER_LIST, responseFrame.Header.PacketId);

            var response = PacketCodec.Decode(responseFrame, GatewayBackendServerListResponse.Codec);
            Assert.True(response.Success);
            Assert.Equal("alpha", response.BackendKind);
            var entry = Assert.Single(response.Entries);
            Assert.Equal(new GatewayBackendServerHandle("server-alpha"), entry.ServerHandle);
            Assert.Equal(GatewayBackendServerState.Open, entry.State);
            Assert.Equal("test-v1", entry.DescriptorVersion);
            Assert.Equal("{\"name\":\"Alpha\"}", entry.DescriptorJson);
            Assert.DoesNotContain("127.0.0.1", entry.DescriptorJson, StringComparison.Ordinal);
            Assert.Equal(0, routeManager.ConnectCount);
        }
        finally
        {
            await connectionManager.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task RouteOpenRequest_UsesServerHandleFromServerList()
    {
        var routeManager = new RecordingBackendRouteManager();
        var connectionManager = CreateConnectionManager(
            routeManager,
            new BackendRouteOptions
            {
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

            await WriteBackendServerListRequestAsync(stream, new GatewayBackendServerListRequest("alpha"));
            using var listFrame = await ReadRequiredFrameAsync(stream);
            var list = PacketCodec.Decode(listFrame, GatewayBackendServerListResponse.Codec);
            var handle = Assert.Single(list.Entries).ServerHandle;

            await WriteBackendRouteOpenRequestAsync(
                stream,
                new GatewayBackendRouteOpenRequest("alpha", handle));

            using var acceptedFrame = await ReadRequiredFrameAsync(stream);
            Assert.Equal(PacketKind.Response, acceptedFrame.Header.Kind);
            Assert.Equal(Pid.GATE_BACKEND_ROUTE_OPEN, acceptedFrame.Header.PacketId);

            var accepted = PacketCodec.Decode(acceptedFrame, GatewayBackendRouteOpenResponse.Codec);
            Assert.True(accepted.Success);
            Assert.Equal(handle, accepted.ServerHandle);
            Assert.Equal("test-v1", accepted.DescriptorVersion);
            Assert.Equal(1, routeManager.ConnectCount);

            var opened = await routeManager.RelayedOpenFrame.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(routeManager.DefaultBinding, opened.Binding);
        }
        finally
        {
            await connectionManager.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task ClientAuthenticateRequest_RejectsWhenTokenValidatorIsNotConfigured()
    {
        var routeManager = new RecordingBackendRouteManager();
        var connectionManager = CreateConnectionManager(
            routeManager,
            new BackendRouteOptions
            {
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

            await WriteClientAuthenticateRequestAsync(stream, new GatewayClientAuthenticateRequest("access-token-alpha"));

            using var rejectedFrame = await ReadRequiredFrameAsync(stream);
            Assert.Equal(PacketKind.Response, rejectedFrame.Header.Kind);
            Assert.Equal(Pid.GATE_CLIENT_AUTHENTICATE, rejectedFrame.Header.PacketId);

            var rejected = PacketCodec.Decode(rejectedFrame, GatewayClientAuthenticateResponse.Codec);
            Assert.False(rejected.Success);
            Assert.Equal(string.Empty, rejected.SubjectId);
            Assert.Equal("Gateway client token validator is not configured.", rejected.ErrorMessage);

            await WriteBackendRouteOpenRequestAsync(stream, new GatewayBackendRouteOpenRequest("alpha"));

            using var routeRejectedFrame = await ReadRequiredFrameAsync(stream);
            Assert.Equal(PacketKind.Response, routeRejectedFrame.Header.Kind);
            Assert.Equal(Pid.GATE_BACKEND_ROUTE_OPEN, routeRejectedFrame.Header.PacketId);

            var routeRejected = PacketCodec.Decode(routeRejectedFrame, GatewayBackendRouteOpenResponse.Codec);
            Assert.False(routeRejected.Success);
            Assert.Equal("Client is not authenticated.", routeRejected.ErrorMessage);
            Assert.Equal(0, routeManager.ConnectCount);
        }
        finally
        {
            await connectionManager.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task ClientAuthenticateRequest_AllowsRouteOpenAfterValidToken()
    {
        var routeManager = new RecordingBackendRouteManager();
        var connectionManager = CreateConnectionManager(
            routeManager,
            new BackendRouteOptions
            {
                RequestTimeoutMilliseconds = 5000,
                MaxOpenRoutes = 8,
                MaxOpenRoutesPerClient = 2,
                RouteLifetimeMilliseconds = 5000
            },
            out var port,
            authenticateClients: false,
            clientTokenValidator: new StaticGatewayClientTokenValidator("access-token-alpha", "player-1"));

        await connectionManager.StartAsync(CancellationToken.None);

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);
            await using var stream = client.GetStream();
            using (await ReadRequiredFrameAsync(stream))
            {
            }

            await WriteClientAuthenticateRequestAsync(stream, new GatewayClientAuthenticateRequest("access-token-alpha"));

            using var acceptedAuthFrame = await ReadRequiredFrameAsync(stream);
            Assert.Equal(PacketKind.Response, acceptedAuthFrame.Header.Kind);
            Assert.Equal(Pid.GATE_CLIENT_AUTHENTICATE, acceptedAuthFrame.Header.PacketId);

            var acceptedAuth = PacketCodec.Decode(acceptedAuthFrame, GatewayClientAuthenticateResponse.Codec);
            Assert.True(acceptedAuth.Success);
            Assert.Equal("player-1", acceptedAuth.SubjectId);
            Assert.Equal(string.Empty, acceptedAuth.ErrorMessage);

            await WriteBackendRouteOpenRequestAsync(stream, new GatewayBackendRouteOpenRequest("alpha"));

            using var acceptedRouteFrame = await ReadRequiredFrameAsync(stream);
            Assert.Equal(PacketKind.Response, acceptedRouteFrame.Header.Kind);
            Assert.Equal(Pid.GATE_BACKEND_ROUTE_OPEN, acceptedRouteFrame.Header.PacketId);

            var acceptedRoute = PacketCodec.Decode(acceptedRouteFrame, GatewayBackendRouteOpenResponse.Codec);
            Assert.True(acceptedRoute.Success);
            Assert.Equal("alpha", acceptedRoute.BackendKind);
            Assert.NotNull(acceptedRoute.RouteToken);
            Assert.Equal(1, routeManager.ConnectCount);

            var opened = await routeManager.RelayedOpenFrame.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("alpha", opened.BackendKind);
            Assert.Equal(routeManager.DefaultBinding, opened.Binding);
            Assert.NotEqual(0u, opened.Open.ChannelId);
            Assert.Equal("player-1", opened.Open.PrincipalSubjectId);
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
            Assert.Equal(1, routeManager.RelayCount);

            var opened = await routeManager.RelayedOpenFrame.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("alpha", opened.BackendKind);
            Assert.Equal(routeManager.DefaultBinding, opened.Binding);
            Assert.NotEqual(0u, opened.Open.ChannelId);
            Assert.Equal("test-client", opened.Open.PrincipalSubjectId);

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
    public async Task RouteOpenRequest_AcceptsLegacyBackendKindOnlyRequest()
    {
        var routeManager = new RecordingBackendRouteManager();
        var connectionManager = CreateConnectionManager(
            routeManager,
            new BackendRouteOptions
            {
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

            await WriteBackendRouteOpenRequestAsync(
                stream,
                new GatewayBackendRouteOpenRequest(" alpha "),
                GatewayBackendRouteOpenRequest.LegacyProtocolVersion,
                GatewayBackendRouteOpenRequest.LegacyCodec);

            using var acceptedFrame = await ReadRequiredFrameAsync(stream);
            Assert.Equal(PacketKind.Response, acceptedFrame.Header.Kind);
            Assert.Equal(Pid.GATE_BACKEND_ROUTE_OPEN, acceptedFrame.Header.PacketId);
            Assert.Equal(GatewayBackendRouteOpenResponse.LegacyProtocolVersion, acceptedFrame.Header.Version);

            var accepted = PacketCodec.Decode(acceptedFrame, GatewayBackendRouteOpenResponse.LegacyCodec);
            Assert.True(accepted.Success);
            Assert.Equal("alpha", accepted.BackendKind);
            Assert.NotNull(accepted.RouteToken);
            Assert.Null(accepted.ServerHandle);
            Assert.Equal(string.Empty, accepted.DescriptorVersion);
            Assert.Equal(string.Empty, accepted.DescriptorHash);
            Assert.Equal(1, routeManager.ConnectCount);

            var opened = await routeManager.RelayedOpenFrame.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("alpha", opened.BackendKind);
            Assert.Equal(routeManager.DefaultBinding, opened.Binding);
        }
        finally
        {
            await connectionManager.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task RouteOpenRequest_RejectsWhenBackendManifestIsMissing()
    {
        var routeManager = new RecordingBackendRouteManager();
        var connectionManager = CreateConnectionManager(
            routeManager,
            new BackendRouteOptions
            {
                RequestTimeoutMilliseconds = 5000,
                MaxOpenRoutes = 8,
                RouteLifetimeMilliseconds = 5000
            },
            out var port,
            backendPacketManifestProvider: new MissingBackendPacketManifestProvider());

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
            Assert.Equal(1, routeManager.ConnectCount);
            Assert.Equal(0, routeManager.RelayCount);

            var completed = await Task.WhenAny(
                routeManager.RelayedOpenFrame.Task,
                Task.Delay(TimeSpan.FromMilliseconds(250)));
            Assert.NotSame(routeManager.RelayedOpenFrame.Task, completed);
        }
        finally
        {
            await connectionManager.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task RouteDataNotify_RejectsUnknownPacketIdFromClientManifest()
    {
        var manifest = CreatePacketManifest(
            CreateManifestEntry(BackendPacketManifestDirection.ClientToBackend, PacketKind.Notify, 101, 1, 0, 8));
        var routeManager = new RecordingBackendRouteManager(manifest);
        var connectionManager = CreateConnectionManager(
            routeManager,
            new BackendRouteOptions
            {
                RequestTimeoutMilliseconds = 5000,
                MaxOpenRoutes = 8,
                RouteLifetimeMilliseconds = 5000
            },
            out var port,
            backendPacketManifestProvider: new StaticBackendPacketManifestProvider(manifest));

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
                GatewayBackendRouteDirection.ClientToBackend,
                PacketKind.Notify,
                routedPacketId: 999,
                routedVersion: 1,
                exchangeId: null,
                [1, 2, 3]);
            await WriteBackendRouteDataEnvelopeAsync(stream, PacketKind.Notify, dataEnvelope);

            await AssertNoDataRelayAsync(routeManager);
            Assert.Contains(connectionManager.GetStatusItems(), item =>
                item.Group == "Backend packet manifest rejects" &&
                item.Name == "ClientToBackend:UnknownPacketId" &&
                item.Value == "1");
        }
        finally
        {
            await connectionManager.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task RouteDataNotify_AllowsCppBackendKindWithManifestPolicy()
    {
        var manifest = CreatePacketManifest(
            "cpp-world",
            CreateManifestEntry(BackendPacketManifestDirection.ClientToBackend, PacketKind.Notify, 101, 1, 0, 8));
        var routeManager = new RecordingBackendRouteManager("cpp-world", manifest);
        var connectionManager = CreateConnectionManager(
            routeManager,
            new BackendRouteOptions
            {
                RequestTimeoutMilliseconds = 5000,
                MaxOpenRoutes = 8,
                RouteLifetimeMilliseconds = 5000
            },
            out var port,
            allowedBackendKinds: ["cpp-world"],
            backendPacketManifestProvider: new StaticBackendPacketManifestProvider(manifest));

        await connectionManager.StartAsync(CancellationToken.None);

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);
            await using var stream = client.GetStream();
            using (await ReadRequiredFrameAsync(stream))
            {
            }

            var routeToken = await OpenPersistentBackendRouteAsync(stream, " cpp-world ");
            byte[] payload = [1, 2, 3, 4];
            var dataEnvelope = new GatewayBackendRouteDataEnvelope(
                routeToken,
                GatewayBackendRouteDirection.ClientToBackend,
                PacketKind.Notify,
                routedPacketId: 101,
                routedVersion: 1,
                exchangeId: null,
                payload);

            await WriteBackendRouteDataEnvelopeAsync(stream, PacketKind.Notify, dataEnvelope);

            var relayed = await routeManager.RelayedDataFrame.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("cpp-world", relayed.BackendKind);
            Assert.Equal(routeManager.DefaultBinding, relayed.Binding);
            Assert.NotEqual(0u, relayed.Envelope.ChannelId);
            Assert.Equal(PacketKind.Notify, relayed.Envelope.RoutedKind);
            Assert.Equal((ushort)101, relayed.Envelope.RoutedPacketId);
            Assert.Equal((ushort)1, relayed.Envelope.RoutedVersion);
            Assert.False(relayed.Envelope.ExchangeId.HasValue);
            Assert.Equal(payload, relayed.Envelope.RoutedPayload);
            Assert.Equal(2, routeManager.RelayCount);
        }
        finally
        {
            await connectionManager.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task RouteDataNotify_RejectsVerifierFailureFromClientManifest()
    {
        var verifierProgram = new BackendPacketVerifierProgram(
            [
                BackendPacketVerifierInstruction.ReadUInt8(
                    targetSlot: 0,
                    valueConstraint: new BackendPacketVerifierValueConstraint(
                        minimumValue: 0,
                        maximumValue: 2)),
                BackendPacketVerifierInstruction.ReadBytes(
                    BackendPacketVerifierLengthConstraint.Dynamic(
                        sourceSlot: 0,
                        minimumLength: 0,
                        maximumLength: 2))
            ]);
        var manifest = CreatePacketManifest(
            CreateManifestEntry(
                BackendPacketManifestDirection.ClientToBackend,
                PacketKind.Notify,
                101,
                1,
                1,
                3,
                verifierProgram));
        var routeManager = new RecordingBackendRouteManager(manifest);
        var connectionManager = CreateConnectionManager(
            routeManager,
            new BackendRouteOptions
            {
                RequestTimeoutMilliseconds = 5000,
                MaxOpenRoutes = 8,
                RouteLifetimeMilliseconds = 5000
            },
            out var port,
            backendPacketManifestProvider: new StaticBackendPacketManifestProvider(manifest));

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
                GatewayBackendRouteDirection.ClientToBackend,
                PacketKind.Notify,
                routedPacketId: 101,
                routedVersion: 1,
                exchangeId: null,
                [1, 7, 9]);
            await WriteBackendRouteDataEnvelopeAsync(stream, PacketKind.Notify, dataEnvelope);

            await AssertNoDataRelayAsync(routeManager);
            Assert.Contains(connectionManager.GetStatusItems(), item =>
                item.Group == "Backend packet manifest rejects" &&
                item.Name == "ClientToBackend:VerifierTrailingBytes" &&
                item.Value == "1");
        }
        finally
        {
            await connectionManager.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task BackendRouteDataNotify_RejectsOversizedBackendPayloadFromManifest()
    {
        var manifest = CreatePacketManifest(
            CreateManifestEntry(BackendPacketManifestDirection.BackendToClient, PacketKind.Notify, 201, 1, 0, 2));
        var routeManager = new RecordingBackendRouteManager(manifest);
        var connectionManager = CreateConnectionManager(
            routeManager,
            new BackendRouteOptions
            {
                RequestTimeoutMilliseconds = 5000,
                MaxOpenRoutes = 8,
                RouteLifetimeMilliseconds = 5000
            },
            out var port,
            backendPacketManifestProvider: new StaticBackendPacketManifestProvider(manifest));

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
                routedPacketId: 201,
                routedVersion: 1,
                exchangeId: null,
                [1, 2, 3]);
            await routeManager.PublishBackendRouteDataFrameAsync("alpha", envelope);

            await AssertNoClientFrameAsync(stream);
            Assert.Contains(connectionManager.GetStatusItems(), item =>
                item.Group == "Backend packet manifest rejects" &&
                item.Name == "BackendToClient:PayloadTooLarge" &&
                item.Value == "1");
        }
        finally
        {
            await connectionManager.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task RouteOpenRequest_RateLimitsBeforeBackendConnect()
    {
        var routeManager = new RecordingBackendRouteManager();
        var connectionManager = CreateConnectionManager(
            routeManager,
            new BackendRouteOptions
            {
                RequestTimeoutMilliseconds = 5000,
                MaxOpenRoutes = 8,
                MaxOpenRoutesPerClient = 8,
                RouteLifetimeMilliseconds = 5000,
                RouteOpenRateLimitWindowMilliseconds = 10000,
                MaxRouteOpenAttemptsPerClientPerWindow = 1
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
            using (var acceptedFrame = await ReadRequiredFrameAsync(stream))
            {
                var accepted = PacketCodec.Decode(acceptedFrame, GatewayBackendRouteOpenResponse.Codec);
                Assert.True(accepted.Success);
            }

            Assert.Equal(1, routeManager.ConnectCount);

            await WriteBackendRouteOpenRequestAsync(stream, new GatewayBackendRouteOpenRequest("alpha"));
            using (var rejectedFrame = await ReadRequiredFrameAsync(stream))
            {
                Assert.Equal(PacketKind.Response, rejectedFrame.Header.Kind);
                Assert.Equal(Pid.GATE_BACKEND_ROUTE_OPEN, rejectedFrame.Header.PacketId);

                var rejected = PacketCodec.Decode(rejectedFrame, GatewayBackendRouteOpenResponse.Codec);
                Assert.False(rejected.Success);
                Assert.Equal("alpha", rejected.BackendKind);
                Assert.Null(rejected.RouteToken);
                Assert.Equal("Backend route open was rejected.", rejected.ErrorMessage);
            }

            Assert.Equal(1, routeManager.ConnectCount);
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
    public async Task RouteOpenRequest_RateLimitsByPrincipalBeforeBackendConnect()
    {
        var routeManager = new RecordingBackendRouteManager();
        var connectionManager = CreateConnectionManager(
            routeManager,
            new BackendRouteOptions
            {
                RequestTimeoutMilliseconds = 5000,
                MaxOpenRoutes = 8,
                MaxOpenRoutesPerClient = 8,
                MaxOpenRoutesPerPrincipal = 8,
                RouteLifetimeMilliseconds = 5000,
                RouteOpenRateLimitWindowMilliseconds = 10000,
                MaxRouteOpenAttemptsPerClientPerWindow = 0,
                MaxRouteOpenAttemptsPerPrincipalPerWindow = 1
            },
            out var port);

        await connectionManager.StartAsync(CancellationToken.None);

        try
        {
            using var firstClient = new TcpClient();
            await firstClient.ConnectAsync(IPAddress.Loopback, port);
            await using var firstStream = firstClient.GetStream();
            using (await ReadRequiredFrameAsync(firstStream))
            {
            }

            await WriteBackendRouteOpenRequestAsync(firstStream, new GatewayBackendRouteOpenRequest("alpha"));
            using (var acceptedFrame = await ReadRequiredFrameAsync(firstStream))
            {
                var accepted = PacketCodec.Decode(acceptedFrame, GatewayBackendRouteOpenResponse.Codec);
                Assert.True(accepted.Success);
            }

            Assert.Equal(1, routeManager.ConnectCount);

            using var secondClient = new TcpClient();
            await secondClient.ConnectAsync(IPAddress.Loopback, port);
            await using var secondStream = secondClient.GetStream();
            using (await ReadRequiredFrameAsync(secondStream))
            {
            }

            await WriteBackendRouteOpenRequestAsync(secondStream, new GatewayBackendRouteOpenRequest("alpha"));
            using (var rejectedFrame = await ReadRequiredFrameAsync(secondStream))
            {
                var rejected = PacketCodec.Decode(rejectedFrame, GatewayBackendRouteOpenResponse.Codec);
                Assert.False(rejected.Success);
                Assert.Equal("alpha", rejected.BackendKind);
                Assert.Equal("Backend route open was rejected.", rejected.ErrorMessage);
            }

            Assert.Equal(1, routeManager.ConnectCount);
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
                RequestTimeoutMilliseconds = 5000
            },
            out var port,
            allowedBackendKinds: []);

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
            Assert.Equal(routeManager.DefaultBinding, relayed.Binding);
            Assert.NotEqual(0u, relayed.Envelope.ChannelId);
            Assert.Equal(PacketKind.Notify, relayed.Envelope.RoutedKind);
            Assert.Equal((ushort)301, relayed.Envelope.RoutedPacketId);
            Assert.Equal((ushort)2, relayed.Envelope.RoutedVersion);
            Assert.False(relayed.Envelope.ExchangeId.HasValue);
            Assert.Equal(payload, relayed.Envelope.RoutedPayload);
            Assert.Equal(2, routeManager.RelayCount);
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
            Assert.NotEqual(0u, relayed.Envelope.ChannelId);
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
    public async Task RouteDataRequest_RateLimitsClientOriginExchangeCreationByPrincipal()
    {
        var routeManager = new RecordingBackendRouteManager();
        var connectionManager = CreateConnectionManager(
            routeManager,
            new BackendRouteOptions
            {
                RequestTimeoutMilliseconds = 5000,
                MaxOpenRoutes = 8,
                MaxOpenRoutesPerClient = 8,
                MaxOpenRoutesPerPrincipal = 8,
                RouteLifetimeMilliseconds = 5000,
                ExchangeRateLimitWindowMilliseconds = 10000,
                MaxClientOriginExchangeCreatesPerRoutePerWindow = 0,
                MaxClientOriginExchangeCreatesPerPrincipalPerWindow = 1
            },
            out var port);

        await connectionManager.StartAsync(CancellationToken.None);

        try
        {
            using var firstClient = new TcpClient();
            await firstClient.ConnectAsync(IPAddress.Loopback, port);
            await using var firstStream = firstClient.GetStream();
            using (await ReadRequiredFrameAsync(firstStream))
            {
            }

            var firstRouteToken = await OpenPersistentBackendRouteAsync(firstStream);

            using var secondClient = new TcpClient();
            await secondClient.ConnectAsync(IPAddress.Loopback, port);
            await using var secondStream = secondClient.GetStream();
            using (await ReadRequiredFrameAsync(secondStream))
            {
            }

            var secondRouteToken = await OpenPersistentBackendRouteAsync(secondStream);

            var firstEnvelope = new GatewayBackendRouteDataEnvelope(
                firstRouteToken,
                GatewayBackendRouteDirection.ClientToBackend,
                PacketKind.Request,
                routedPacketId: 401,
                routedVersion: 3,
                new GatewayBackendExchangeId(Guid.NewGuid()),
                [1, 2, 3]);
            await WriteBackendRouteDataEnvelopeAsync(firstStream, PacketKind.Request, firstEnvelope);
            await routeManager.RelayedDataFrame.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(3, routeManager.RelayCount);

            var secondEnvelope = new GatewayBackendRouteDataEnvelope(
                secondRouteToken,
                GatewayBackendRouteDirection.ClientToBackend,
                PacketKind.Request,
                routedPacketId: 402,
                routedVersion: 3,
                new GatewayBackendExchangeId(Guid.NewGuid()),
                [4, 5, 6]);
            await WriteBackendRouteDataEnvelopeAsync(secondStream, PacketKind.Request, secondEnvelope);
            await Task.Delay(TimeSpan.FromMilliseconds(250));

            Assert.Equal(3, routeManager.RelayCount);
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
            Assert.Equal(2, routeManager.RelayCount);

            await WriteBackendRouteDataEnvelopeAsync(stream, PacketKind.Request, duplicateEnvelope);
            await Task.Delay(TimeSpan.FromMilliseconds(250));

            Assert.Equal(2, routeManager.RelayCount);
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
    public async Task RouteDataRequest_RejectsPendingExchangeCapacity()
    {
        var routeManager = new RecordingBackendRouteManager();
        var connectionManager = CreateConnectionManager(
            routeManager,
            new BackendRouteOptions
            {
                RequestTimeoutMilliseconds = 5000,
                RouteLifetimeMilliseconds = 5000,
                MaxPendingExchangesPerRoutePerDirection = 1
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
            var firstEnvelope = new GatewayBackendRouteDataEnvelope(
                routeToken,
                GatewayBackendRouteDirection.ClientToBackend,
                PacketKind.Request,
                routedPacketId: 401,
                routedVersion: 3,
                new GatewayBackendExchangeId(Guid.NewGuid()),
                [1]);
            var rejectedEnvelope = new GatewayBackendRouteDataEnvelope(
                routeToken,
                GatewayBackendRouteDirection.ClientToBackend,
                PacketKind.Request,
                routedPacketId: 402,
                routedVersion: 3,
                new GatewayBackendExchangeId(Guid.NewGuid()),
                [2]);

            await WriteBackendRouteDataEnvelopeAsync(stream, PacketKind.Request, firstEnvelope);
            await routeManager.RelayedDataFrame.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(2, routeManager.RelayCount);

            await WriteBackendRouteDataEnvelopeAsync(stream, PacketKind.Request, rejectedEnvelope);
            await Task.Delay(TimeSpan.FromMilliseconds(250));

            Assert.Equal(2, routeManager.RelayCount);
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
            Assert.NotEqual(0u, relayed.Envelope.ChannelId);
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
    public async Task BackendRouteDataResponse_RejectsExpiredClientOriginExchange()
    {
        var routeManager = new RecordingBackendRouteManager();
        var connectionManager = CreateConnectionManager(
            routeManager,
            new BackendRouteOptions
            {
                RequestTimeoutMilliseconds = 5000,
                RouteLifetimeMilliseconds = 5000,
                ExchangeTimeoutMilliseconds = 25
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
            await Task.Delay(TimeSpan.FromMilliseconds(75));

            var responseEnvelope = new GatewayBackendRouteDataEnvelope(
                routeToken,
                GatewayBackendRouteDirection.BackendToClient,
                PacketKind.Response,
                routedPacketId: 402,
                routedVersion: 3,
                exchangeId,
                [4, 5, 6]);
            await routeManager.PublishBackendRouteDataFrameAsync("alpha", responseEnvelope);

            await AssertNoClientFrameAsync(stream);
            Assert.Contains(connectionManager.GetStatusItems(), item =>
                item.Group == "Persistent Backend routes" &&
                item.Name == "Open routes" &&
                item.Value == "1");
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
    public async Task ClientRouteDataResponse_RejectsExpiredBackendOriginExchange()
    {
        var routeManager = new RecordingBackendRouteManager();
        var connectionManager = CreateConnectionManager(
            routeManager,
            new BackendRouteOptions
            {
                RequestTimeoutMilliseconds = 5000,
                RouteLifetimeMilliseconds = 5000,
                ExchangeTimeoutMilliseconds = 25
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
            var backendRequest = new GatewayBackendRouteDataEnvelope(
                routeToken,
                GatewayBackendRouteDirection.BackendToClient,
                PacketKind.Request,
                routedPacketId: 601,
                routedVersion: 4,
                exchangeId,
                [8, 8, 8]);
            await routeManager.PublishBackendRouteDataFrameAsync("alpha", backendRequest);

            using (await ReadRequiredFrameAsync(stream))
            {
            }

            await Task.Delay(TimeSpan.FromMilliseconds(75));
            var responseEnvelope = new GatewayBackendRouteDataEnvelope(
                routeToken,
                GatewayBackendRouteDirection.ClientToBackend,
                PacketKind.Response,
                routedPacketId: 602,
                routedVersion: 4,
                exchangeId,
                [9, 9]);
            await WriteBackendRouteDataEnvelopeAsync(stream, PacketKind.Response, responseEnvelope);

            await AssertNoDataRelayAsync(routeManager);
            Assert.Contains(connectionManager.GetStatusItems(), item =>
                item.Group == "Persistent Backend routes" &&
                item.Name == "Open routes" &&
                item.Value == "1");
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
    public async Task BackendRouteData_RejectsBackendSessionMismatch()
    {
        var routeManager = new RecordingBackendRouteManager();
        var connectionManager = CreateConnectionManager(
            routeManager,
            new BackendRouteOptions
            {
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

            await routeManager.PublishBackendRouteDataFrameAsync(
                "alpha",
                envelope,
                directConnectionId: "backend-direct-b");

            await AssertNoClientFrameAsync(stream);
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
    public async Task RouteCloseRequest_ClosesRouteAndAcknowledges()
    {
        var routeManager = new RecordingBackendRouteManager();
        var connectionManager = CreateConnectionManager(
            routeManager,
            new BackendRouteOptions
            {
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
            var relayedClose = await routeManager.RelayedCloseFrame.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(routeManager.DefaultBinding, relayedClose.Binding);
            Assert.NotEqual(0u, relayedClose.Close.ChannelId);
            Assert.Equal("client closed", relayedClose.Close.Reason);

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
            Assert.NotEqual(0u, relayed.Envelope.ChannelId);
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
    public async Task BackendRouteClose_ClosesRouteAndNotifiesOwningClient()
    {
        var routeManager = new RecordingBackendRouteManager();
        var connectionManager = CreateConnectionManager(
            routeManager,
            new BackendRouteOptions
            {
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
            await routeManager.PublishBackendRouteCloseFrameAsync(
                "alpha",
                new GatewayBackendRouteClose(routeToken, "backend closed"));

            using (var closeFrame = await ReadRequiredFrameAsync(stream))
            {
                Assert.Equal(PacketKind.Notify, closeFrame.Header.Kind);
                Assert.Equal(Pid.GATE_BACKEND_ROUTE_CLOSE, closeFrame.Header.PacketId);

                var close = PacketCodec.Decode(closeFrame, GatewayBackendRouteClose.Codec);
                Assert.Equal(routeToken, close.RouteToken);
                Assert.Equal("backend closed", close.Reason);
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
    public async Task BackendRouteClose_ClearsPendingExchanges()
    {
        var routeManager = new RecordingBackendRouteManager();
        var connectionManager = CreateConnectionManager(
            routeManager,
            new BackendRouteOptions
            {
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

            await routeManager.PublishBackendRouteCloseFrameAsync(
                "alpha",
                new GatewayBackendRouteClose(routeToken, "backend closed"));
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
    public async Task BackendRouteClose_IgnoresUnknownChannelId()
    {
        var routeManager = new RecordingBackendRouteManager();
        var connectionManager = CreateConnectionManager(
            routeManager,
            new BackendRouteOptions
            {
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
            await routeManager.PublishBackendRouteCloseFrameAsync(
                "alpha",
                new GatewayBackendChannelClose(999, "backend closed"));

            await AssertNoClientFrameAsync(stream);
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
            await WriteBackendRouteDataEnvelopeAsync(stream, PacketKind.Notify, dataEnvelope);

            var relayed = await routeManager.RelayedDataFrame.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.NotEqual(0u, relayed.Envelope.ChannelId);
            Assert.Equal(PacketKind.Notify, relayed.Envelope.RoutedKind);
        }
        finally
        {
            await connectionManager.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task BackendRouteClose_RejectsBackendKindMismatch()
    {
        var routeManager = new RecordingBackendRouteManager();
        var connectionManager = CreateConnectionManager(
            routeManager,
            new BackendRouteOptions
            {
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
            await routeManager.PublishBackendRouteCloseFrameAsync(
                "beta",
                new GatewayBackendRouteClose(routeToken, "backend closed"));

            await AssertNoClientFrameAsync(stream);
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
    public async Task BackendRouteSessionClosed_ClosesBoundRoutesAndNotifiesClient()
    {
        var routeManager = new RecordingBackendRouteManager();
        var connectionManager = CreateConnectionManager(
            routeManager,
            new BackendRouteOptions
            {
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
            await routeManager.PublishBackendRouteSessionClosedAsync(
                routeManager.DefaultBinding,
                "backend disconnected");

            using (var closeFrame = await ReadRequiredFrameAsync(stream))
            {
                Assert.Equal(PacketKind.Notify, closeFrame.Header.Kind);
                Assert.Equal(Pid.GATE_BACKEND_ROUTE_CLOSE, closeFrame.Header.PacketId);

                var close = PacketCodec.Decode(closeFrame, GatewayBackendRouteClose.Codec);
                Assert.Equal(routeToken, close.RouteToken);
                Assert.Equal("backend disconnected", close.Reason);
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
    public async Task BackendRouteSessionClosed_IgnoresDifferentBackendSession()
    {
        var routeManager = new RecordingBackendRouteManager();
        var connectionManager = CreateConnectionManager(
            routeManager,
            new BackendRouteOptions
            {
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
            await routeManager.PublishBackendRouteSessionClosedAsync(new BackendRouteBinding(
                "alpha",
                "backend-a",
                "master-a",
                "backend-direct-b"));

            await AssertNoClientFrameAsync(stream);
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
            await WriteBackendRouteDataEnvelopeAsync(stream, PacketKind.Notify, dataEnvelope);

            var relayed = await routeManager.RelayedDataFrame.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(routeManager.DefaultBinding, relayed.Binding);
            Assert.NotEqual(0u, relayed.Envelope.ChannelId);
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
    public async Task ClientIdleTimeout_DisconnectsIdleClient()
    {
        var routeManager = new RecordingBackendRouteManager();
        var connectionManager = CreateConnectionManager(
            routeManager,
            new BackendRouteOptions
            {
            },
            out var port,
            connectionOptions: new ConnectionManagerOptions
            {
                IPAddress = "127.0.0.1",
                ClientIdleTimeoutMilliseconds = 25,
                ClientAuthenticationTimeoutMilliseconds = 0
            });

        await connectionManager.StartAsync(CancellationToken.None);

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);
            await using var stream = client.GetStream();
            using (await ReadRequiredFrameAsync(stream))
            {
            }

            using var disconnectedFrame = await ReadOptionalFrameAsync(stream, TimeSpan.FromSeconds(5));
            Assert.Null(disconnectedFrame);
        }
        finally
        {
            await connectionManager.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task ClientAuthenticationTimeout_DisconnectsUnauthenticatedClient()
    {
        var routeManager = new RecordingBackendRouteManager();
        var connectionManager = CreateConnectionManager(
            routeManager,
            new BackendRouteOptions
            {
            },
            out var port,
            connectionOptions: new ConnectionManagerOptions
            {
                IPAddress = "127.0.0.1",
                ClientIdleTimeoutMilliseconds = 0,
                ClientAuthenticationTimeoutMilliseconds = 25
            },
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

            using var disconnectedFrame = await ReadOptionalFrameAsync(stream, TimeSpan.FromSeconds(5));
            Assert.Null(disconnectedFrame);
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
        IGatewayClientCertificateProvider? certificateProvider = null,
        IGatewayClientStreamAuthenticator? streamAuthenticator = null,
        bool authenticateClients = true,
        IGatewayClientTokenValidator? clientTokenValidator = null,
        string[]? allowedBackendKinds = null,
        IBackendPacketManifestProvider? backendPacketManifestProvider = null)
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
            new StaticGatewayBackendRoutePolicyProvider(allowedBackendKinds ?? ["alpha"]),
            backendPacketManifestProvider ?? new PermissiveBackendPacketManifestProvider(),
            certificateProvider ?? new StaticCertificateProvider(CreateServerCertificate()),
            streamAuthenticator ?? new PassThroughStreamAuthenticator(),
            new StaticGatewayClientAuthenticationContextFactory(authenticateClients),
            clientTokenValidator ?? new RejectingGatewayClientTokenValidator(),
            new GatewayBackendRouteTokenGenerator(),
            NullLogger<ConnectionManager>.Instance);
    }

    private sealed class StaticGatewayBackendRoutePolicyProvider(string[] allowedBackendKinds)
        : IGatewayBackendRoutePolicyProvider
    {
        private readonly string[] m_AllowedBackendKinds =
        [
            .. allowedBackendKinds
                .Where(static backendKind => !string.IsNullOrWhiteSpace(backendKind))
                .Select(static backendKind => backendKind.Trim())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static backendKind => backendKind, StringComparer.Ordinal)
        ];

        public string[] GetAllowedBackendKinds()
        {
            return [.. m_AllowedBackendKinds];
        }

        public bool IsBackendKindAllowed(string backendKind, out string normalizedBackendKind)
        {
            var normalized = string.IsNullOrWhiteSpace(backendKind)
                ? string.Empty
                : backendKind.Trim();
            normalizedBackendKind = normalized;
            return m_AllowedBackendKinds.Any(candidate => string.Equals(
                candidate,
                normalized,
                StringComparison.Ordinal));
        }
    }

    private sealed class PermissiveBackendPacketManifestProvider : IBackendPacketManifestProvider
    {
        private static readonly BackendPacketManifest s_Manifest = new(
            "alpha",
            RecordingBackendRouteManager.TestManifestId,
            []);

        public BackendPacketManifest RequireManifest(
            string backendKind,
            BackendPacketManifestId manifestId,
            BackendPacketManifestHash manifestHash)
        {
            return s_Manifest;
        }

        public BackendPacketManifestValidationResult ValidatePacket(
            string backendKind,
            BackendPacketManifestId manifestId,
            BackendPacketManifestHash manifestHash,
            BackendPacketManifestDirection direction,
            PacketKind packetKind,
            ushort packetId,
            ushort routedVersion,
            ReadOnlySpan<byte> payload)
        {
            return BackendPacketManifestValidationResult.Accepted(new BackendPacketManifestEntry(
                direction,
                packetKind,
                packetId,
                routedVersion,
                BackendPacketPayloadConstraint.Any));
        }

        public ServiceAdminStatusItem[] GetStatusItems()
        {
            return [];
        }
    }

    private sealed class StaticBackendPacketManifestProvider(BackendPacketManifest manifest)
        : IBackendPacketManifestProvider
    {
        public BackendPacketManifest RequireManifest(
            string backendKind,
            BackendPacketManifestId manifestId,
            BackendPacketManifestHash manifestHash)
        {
            if (manifest.MatchesAdvertisement(backendKind, manifestId, manifestHash))
            {
                return manifest;
            }

            throw new InvalidOperationException("Backend packet manifest is not loaded.");
        }

        public BackendPacketManifestValidationResult ValidatePacket(
            string backendKind,
            BackendPacketManifestId manifestId,
            BackendPacketManifestHash manifestHash,
            BackendPacketManifestDirection direction,
            PacketKind packetKind,
            ushort packetId,
            ushort routedVersion,
            ReadOnlySpan<byte> payload)
        {
            return RequireManifest(backendKind, manifestId, manifestHash)
                .ValidatePacket(direction, packetKind, packetId, routedVersion, payload);
        }

        public ServiceAdminStatusItem[] GetStatusItems()
        {
            return [];
        }
    }

    private sealed class MissingBackendPacketManifestProvider : IBackendPacketManifestProvider
    {
        public BackendPacketManifest RequireManifest(
            string backendKind,
            BackendPacketManifestId manifestId,
            BackendPacketManifestHash manifestHash)
        {
            throw new InvalidOperationException("Backend packet manifest is not loaded.");
        }

        public BackendPacketManifestValidationResult ValidatePacket(
            string backendKind,
            BackendPacketManifestId manifestId,
            BackendPacketManifestHash manifestHash,
            BackendPacketManifestDirection direction,
            PacketKind packetKind,
            ushort packetId,
            ushort routedVersion,
            ReadOnlySpan<byte> payload)
        {
            throw new InvalidOperationException("Backend packet manifest is not loaded.");
        }

        public ServiceAdminStatusItem[] GetStatusItems()
        {
            return [];
        }
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

    private static async Task<PacketFrame?> ReadOptionalFrameAsync(
        Stream stream,
        TimeSpan timeout)
    {
        using var timeoutCancellation = new CancellationTokenSource(timeout);
        try
        {
            return await PacketFrameReader.ReadAsync(
                stream,
                PacketReadPolicy.TrustedServer,
                timeoutCancellation.Token);
        }
        catch (OperationCanceledException) when (timeoutCancellation.IsCancellationRequested)
        {
            throw new TimeoutException("Gateway test connection did not close before the timeout.");
        }
        catch (IOException)
        {
            return null;
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
        catch (SocketException)
        {
            return null;
        }
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
        await WriteBackendRouteOpenRequestAsync(
            stream,
            request,
            GatewayBackendRouteOpenRequest.ProtocolVersion,
            GatewayBackendRouteOpenRequest.Codec);
    }

    private static async Task WriteBackendRouteOpenRequestAsync(
        Stream stream,
        GatewayBackendRouteOpenRequest request,
        ushort protocolVersion,
        IPacketCodec<GatewayBackendRouteOpenRequest> codec)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var frame = PacketCodec.Encode(
            PacketKind.Request,
            Pid.GATE_BACKEND_ROUTE_OPEN,
            protocolVersion,
            request,
            codec);
        await PacketFrameWriter.WriteAsync(stream, frame, timeout.Token);
    }

    private static async Task WriteBackendServerListRequestAsync(
        Stream stream,
        GatewayBackendServerListRequest request)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var frame = PacketCodec.Encode(
            PacketKind.Request,
            Pid.GATE_BACKEND_SERVER_LIST,
            GatewayBackendServerListRequest.ProtocolVersion,
            request,
            GatewayBackendServerListRequest.Codec);
        await PacketFrameWriter.WriteAsync(stream, frame, timeout.Token);
    }

    private static async Task WriteClientAuthenticateRequestAsync(
        Stream stream,
        GatewayClientAuthenticateRequest request)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var frame = PacketCodec.Encode(
            PacketKind.Request,
            Pid.GATE_CLIENT_AUTHENTICATE,
            GatewayClientAuthenticateRequest.ProtocolVersion,
            request,
            GatewayClientAuthenticateRequest.Codec);
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

    private static BackendPacketManifest CreatePacketManifest(params BackendPacketManifestEntry[] entries)
    {
        return CreatePacketManifest("alpha", entries);
    }

    private static BackendPacketManifest CreatePacketManifest(
        string backendKind,
        params BackendPacketManifestEntry[] entries)
    {
        return new BackendPacketManifest(
            backendKind,
            RecordingBackendRouteManager.TestManifestId,
            entries);
    }

    private static BackendPacketManifestEntry CreateManifestEntry(
        BackendPacketManifestDirection direction,
        PacketKind packetKind,
        ushort packetId,
        ushort routedVersion,
        int minimumLength,
        int maximumLength,
        BackendPacketVerifierProgram? verifierProgram = null)
    {
        return new BackendPacketManifestEntry(
            direction,
            packetKind,
            packetId,
            routedVersion,
            new BackendPacketPayloadConstraint(
                minimumLength,
                maximumLength,
                verifierProgram: verifierProgram));
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
        public static readonly BackendPacketManifestId TestManifestId = new("test");
        public static readonly BackendPacketManifestHash TestManifestHash = new("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        private readonly BackendRouteBinding m_DefaultBinding;
        private readonly GatewayBackendServerHandle m_DefaultServerHandle = new("server-alpha");
        private readonly BackendPacketManifestId m_ManifestId;
        private readonly BackendPacketManifestHash m_ManifestHash;
        private readonly object m_TestChannelSync = new();
        private readonly Dictionary<string, uint> m_TestChannelIdsByRouteToken = new(StringComparer.Ordinal);
        private int m_ConnectCount;
        private int m_RelayCount;
        private uint m_NextTestChannelId;

        public event BackendRouteFrameReceivedHandler? RouteFrameReceived;

        public event BackendRouteDataFrameReceivedHandler? RouteDataFrameReceived;

        public event BackendRouteCloseFrameReceivedHandler? RouteCloseFrameReceived;

        public event BackendRouteSessionClosedHandler? RouteSessionClosed;

        public RecordingBackendRouteManager()
            : this(null)
        {
        }

        public RecordingBackendRouteManager(BackendPacketManifest? manifest)
            : this("alpha", manifest)
        {
        }

        public RecordingBackendRouteManager(string backendKind, BackendPacketManifest? manifest = null)
        {
            m_DefaultBinding = new BackendRouteBinding(
                backendKind,
                "backend-a",
                "master-a",
                "backend-direct-a");
            m_ManifestId = manifest?.ManifestId ?? TestManifestId;
            m_ManifestHash = manifest?.Hash ?? TestManifestHash;
        }

        public TaskCompletionSource<ObservedBackendRouteFrame> RelayedFrame { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<ObservedBackendRouteOpenFrame> RelayedOpenFrame { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<ObservedBackendRouteDataFrame> RelayedDataFrame { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<ObservedBackendRouteCloseFrame> RelayedCloseFrame { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public BackendRouteBinding DefaultBinding => m_DefaultBinding;

        public BackendPacketManifestId ManifestId => m_ManifestId;

        public BackendPacketManifestHash ManifestHash => m_ManifestHash;

        public int ConnectCount => Volatile.Read(ref m_ConnectCount);

        public int RelayCount => Volatile.Read(ref m_RelayCount);

        public string[] GetDiscoveredBackendKinds()
        {
            return [m_DefaultBinding.BackendKind];
        }

        public BackendServerDirectorySnapshot ListServers(
            string backendKind,
            int maximumEntries)
        {
            if (!string.Equals(backendKind, m_DefaultBinding.BackendKind, StringComparison.Ordinal))
            {
                return new BackendServerDirectorySnapshot([], DateTimeOffset.UtcNow);
            }

            var descriptor = BackendServerDescriptor.Create(
                BackendNodeState.Open,
                "test-v1",
                "{\"name\":\"Alpha\"}");
            return new BackendServerDirectorySnapshot(
                [
                    new GatewayBackendServerListEntry(
                        m_DefaultServerHandle,
                        m_DefaultBinding.BackendKind,
                        GatewayBackendServerState.Open,
                        descriptor.DescriptorVersion,
                        descriptor.DescriptorHash,
                        descriptor.DescriptorJson)
                ],
                DateTimeOffset.UtcNow);
        }

        public ValueTask<IBackendRouteSession> ConnectAsync(
            string backendKind,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref m_ConnectCount);
            if (!string.Equals(backendKind, m_DefaultBinding.BackendKind, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("No recording Backend route session is available for the requested kind.");
            }

            return ValueTask.FromResult<IBackendRouteSession>(new RecordingBackendRouteSession(this, m_DefaultBinding));
        }

        public ValueTask<IBackendRouteSession> ConnectAsync(
            string backendKind,
            GatewayBackendServerHandle serverHandle,
            CancellationToken cancellationToken)
        {
            if (!m_DefaultServerHandle.Equals(serverHandle))
            {
                throw new InvalidOperationException("No recording Backend route session is available for the requested server handle.");
            }

            return ConnectAsync(backendKind, cancellationToken);
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
            else if (frame.Header.PacketId == Pid.GATE_BACKEND_CHANNEL_OPEN)
            {
                RelayedOpenFrame.TrySetResult(new ObservedBackendRouteOpenFrame(
                    backendKind,
                    null,
                    PacketCodec.Decode(frame, GatewayBackendChannelOpen.Codec)));
            }
            else if (frame.Header.PacketId == Pid.GATE_BACKEND_CHANNEL_DATA)
            {
                RelayedDataFrame.TrySetResult(new ObservedBackendRouteDataFrame(
                    backendKind,
                    null,
                    PacketCodec.Decode(frame, GatewayBackendChannelDataEnvelope.Codec)));
            }
            else if (frame.Header.PacketId == Pid.GATE_BACKEND_CHANNEL_CLOSE)
            {
                RelayedCloseFrame.TrySetResult(new ObservedBackendRouteCloseFrame(
                    backendKind,
                    null,
                    PacketCodec.Decode(frame, GatewayBackendChannelClose.Codec)));
            }
            else
            {
                throw new InvalidOperationException("ConnectionManager relayed an unexpected Backend route packet.");
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask RelayFrameAsync(
            BackendRouteBinding binding,
            PacketFrame frame,
            CancellationToken cancellationToken)
        {
            if (binding == null)
            {
                throw new ArgumentNullException(nameof(binding));
            }

            Interlocked.Increment(ref m_RelayCount);
            if (frame.Header.PacketId == Pid.GATE_BACKEND_CHANNEL_OPEN)
            {
                RelayedOpenFrame.TrySetResult(new ObservedBackendRouteOpenFrame(
                    binding.BackendKind,
                    binding,
                    PacketCodec.Decode(frame, GatewayBackendChannelOpen.Codec)));

                return ValueTask.CompletedTask;
            }

            if (frame.Header.PacketId == Pid.GATE_BACKEND_CHANNEL_DATA)
            {
                RelayedDataFrame.TrySetResult(new ObservedBackendRouteDataFrame(
                    binding.BackendKind,
                    binding,
                    PacketCodec.Decode(frame, GatewayBackendChannelDataEnvelope.Codec)));

                return ValueTask.CompletedTask;
            }

            if (frame.Header.PacketId == Pid.GATE_BACKEND_CHANNEL_CLOSE)
            {
                RelayedCloseFrame.TrySetResult(new ObservedBackendRouteCloseFrame(
                    binding.BackendKind,
                    binding,
                    PacketCodec.Decode(frame, GatewayBackendChannelClose.Codec)));

                return ValueTask.CompletedTask;
            }

            throw new InvalidOperationException("ConnectionManager relayed an unexpected bound Backend route packet.");
        }

        public async Task PublishBackendRouteFrameAsync(GatewayBackendRouteEnvelope envelope)
        {
            var frame = new BackendRouteFrameReceived(
                envelope.BackendKind,
                nodeId: "backend-a",
                masterConnectionId: "master-a",
                directConnectionId: "backend-direct-a",
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
            GatewayBackendRouteDataEnvelope envelope,
            string nodeId = "backend-a",
            string masterConnectionId = "master-a",
            string directConnectionId = "backend-direct-a")
        {
            await PublishBackendRouteDataFrameAsync(
                backendKind,
                CreateChannelDataEnvelope(envelope),
                nodeId,
                masterConnectionId,
                directConnectionId);
        }

        public async Task PublishBackendRouteDataFrameAsync(
            string backendKind,
            GatewayBackendChannelDataEnvelope envelope,
            string nodeId = "backend-a",
            string masterConnectionId = "master-a",
            string directConnectionId = "backend-direct-a")
        {
            var frame = new BackendRouteDataFrameReceived(
                backendKind,
                nodeId,
                masterConnectionId,
                directConnectionId,
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

        public async Task PublishBackendRouteCloseFrameAsync(
            string backendKind,
            GatewayBackendRouteClose close,
            string nodeId = "backend-a",
            string masterConnectionId = "master-a",
            string directConnectionId = "backend-direct-a")
        {
            await PublishBackendRouteCloseFrameAsync(
                backendKind,
                new GatewayBackendChannelClose(GetTestChannelId(close.RouteToken), close.Reason),
                nodeId,
                masterConnectionId,
                directConnectionId);
        }

        public async Task PublishBackendRouteCloseFrameAsync(
            string backendKind,
            GatewayBackendChannelClose close,
            string nodeId = "backend-a",
            string masterConnectionId = "master-a",
            string directConnectionId = "backend-direct-a")
        {
            var frame = new BackendRouteCloseFrameReceived(
                backendKind,
                nodeId,
                masterConnectionId,
                directConnectionId,
                close);
            var handlers = RouteCloseFrameReceived;
            if (handlers == null)
            {
                return;
            }

            foreach (BackendRouteCloseFrameReceivedHandler handler in handlers.GetInvocationList())
            {
                await handler(frame, CancellationToken.None);
            }
        }

        public async Task PublishBackendRouteSessionClosedAsync(
            BackendRouteBinding binding,
            string reason = "backend disconnected")
        {
            var handlers = RouteSessionClosed;
            if (handlers == null)
            {
                return;
            }

            var frame = new BackendRouteSessionClosed(binding, reason);
            foreach (BackendRouteSessionClosedHandler handler in handlers.GetInvocationList())
            {
                await handler(frame, CancellationToken.None);
            }
        }

        private GatewayBackendChannelDataEnvelope CreateChannelDataEnvelope(GatewayBackendRouteDataEnvelope envelope)
        {
            return new GatewayBackendChannelDataEnvelope(
                GetTestChannelId(envelope.RouteToken),
                envelope.RoutedKind,
                envelope.RoutedPacketId,
                envelope.RoutedVersion,
                envelope.ExchangeId,
                envelope.RoutedPayload);
        }

        private uint GetTestChannelId(GatewayBackendRouteToken routeToken)
        {
            lock (m_TestChannelSync)
            {
                if (m_TestChannelIdsByRouteToken.TryGetValue(routeToken.Value, out var channelId))
                {
                    return channelId;
                }

                channelId = ++m_NextTestChannelId;
                m_TestChannelIdsByRouteToken[routeToken.Value] = channelId;
                return channelId;
            }
        }
    }

    private sealed record ObservedBackendRouteFrame(
        string BackendKind,
        GatewayBackendRouteEnvelope Envelope);

    private sealed record ObservedBackendRouteDataFrame(
        string BackendKind,
        BackendRouteBinding? Binding,
        GatewayBackendChannelDataEnvelope Envelope);

    private sealed record ObservedBackendRouteOpenFrame(
        string BackendKind,
        BackendRouteBinding? Binding,
        GatewayBackendChannelOpen Open);

    private sealed record ObservedBackendRouteCloseFrame(
        string BackendKind,
        BackendRouteBinding? Binding,
        GatewayBackendChannelClose Close);

    private sealed class RecordingBackendRouteSession(
        RecordingBackendRouteManager owner,
        BackendRouteBinding binding) : IBackendRouteSession
    {
        public string BackendKind => binding.BackendKind;

        public string NodeId => binding.NodeId;

        public string MasterConnectionId => binding.MasterConnectionId;

        public string DirectConnectionId => binding.DirectConnectionId;

        public BackendRouteBinding Binding => binding;

        public BackendPacketManifestId ManifestId => owner.ManifestId;

        public BackendPacketManifestHash ManifestHash => owner.ManifestHash;

        public GatewayBackendServerHandle? ServerHandle { get; } = new("server-alpha");

        public string DescriptorVersion { get; } = "test-v1";

        public string DescriptorHash { get; } = BackendServerDescriptor.ComputeDescriptorHash("{\"name\":\"Alpha\"}");

        public ValueTask WriteAsync(PacketFrame frame, CancellationToken cancellationToken)
        {
            return owner.RelayFrameAsync(binding, frame, cancellationToken);
        }
    }

    private sealed class StaticCertificateProvider(X509Certificate2 certificate) : IGatewayClientCertificateProvider
    {
        public GatewayClientCertificateLease AcquireLease()
        {
            var provider = new GatewayClientCertificateProvider.CertificateEntry(certificate);
            provider.AddLease();
            return new GatewayClientCertificateLease(provider);
        }

        public GatewayClientCertificateStatus GetStatus()
        {
            return new GatewayClientCertificateStatus(
                certificate.Thumbprint,
                new DateTimeOffset(certificate.NotBefore.ToUniversalTime(), TimeSpan.Zero),
                new DateTimeOffset(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero),
                DateTimeOffset.UtcNow,
                null,
                false);
        }

        public ServiceAdminStatusItem[] GetStatusItems()
        {
            return [];
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

    private sealed class StaticGatewayClientTokenValidator(
        string acceptedAccessToken,
        string subjectId) : IGatewayClientTokenValidator
    {
        public ValueTask<GatewayClientTokenValidationResult> ValidateAsync(
            string accessToken,
            CancellationToken cancellationToken)
        {
            if (string.Equals(accessToken, acceptedAccessToken, StringComparison.Ordinal))
            {
                return ValueTask.FromResult(
                    GatewayClientTokenValidationResult.Accepted(new GatewayClientPrincipal(subjectId)));
            }

            return ValueTask.FromResult(
                GatewayClientTokenValidationResult.Rejected("Invalid Gateway client access token."));
        }
    }

}
