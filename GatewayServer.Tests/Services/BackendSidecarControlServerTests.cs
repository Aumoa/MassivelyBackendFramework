using System.Net;
using System.Net.Sockets;
using BackendServer.Options;
using BackendServer.Services;
using MasterServer.ControlPlane;
using Microsoft.Extensions.Logging;
using PacketCore;
using Xunit;

namespace GatewayServer.Tests.Services;

public sealed class BackendSidecarControlServerTests
{
    [Fact]
    public async Task DirectConnectValidation_ReturnsSuccessfulBackendValidation()
    {
        var port = GetFreeTcpPort();
        var validator = new RecordingDirectConnectCodeValidator(MasterNodeKind.Backend);
        var server = CreateServer(port, validator);
        await server.StartAsync(CancellationToken.None);

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);
            await using var stream = client.GetStream();

            var request = new DirectConnectCodeValidationRequest(
                Guid.NewGuid(),
                "direct-code",
                "gateway-a",
                "gateway-master-a");
            await WriteValidationRequestAsync(stream, request);

            var response = await ReadValidationResponseAsync(stream);
            Assert.True(response.Success);
            Assert.Equal(request.RequestId, response.RequestId);
            Assert.Equal("gateway-a", response.GatewayNodeId);
            Assert.Equal("gateway-master-a", response.GatewayMasterConnectionId);
            Assert.Equal(MasterNodeKind.Backend, response.TargetNodeKind);
            Assert.Equal("backend-local", response.TargetNodeId);
            Assert.Equal("backend-master-a", response.TargetMasterConnectionId);

            var recorded = await validator.Validation.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("direct-code", recorded.Code);
            Assert.Equal("gateway-a", recorded.GatewayNodeId);
            Assert.Equal("gateway-master-a", recorded.GatewayMasterConnectionId);

            var status = server.GetStatusItems();
            Assert.Contains(status, item =>
                item.Group == "Sidecar Control" &&
                item.Name == "Validation requests" &&
                item.Value == "1");
            Assert.Contains(status, item =>
                item.Group == "Sidecar Control" &&
                item.Name == "Validation succeeded" &&
                item.Value == "1");
        }
        finally
        {
            await server.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task DirectConnectValidation_RejectsUnexpectedTargetKind()
    {
        var port = GetFreeTcpPort();
        var server = CreateServer(
            port,
            new RecordingDirectConnectCodeValidator(MasterNodeKind.Dedicated));
        await server.StartAsync(CancellationToken.None);

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);
            await using var stream = client.GetStream();

            var request = new DirectConnectCodeValidationRequest(
                Guid.NewGuid(),
                "direct-code",
                "gateway-a",
                "gateway-master-a");
            await WriteValidationRequestAsync(stream, request);

            var response = await ReadValidationResponseAsync(stream);
            Assert.False(response.Success);
            Assert.Equal(request.RequestId, response.RequestId);
            Assert.Contains("unexpected connection identity", response.ErrorMessage);

            Assert.Contains(server.GetStatusItems(), item =>
                item.Group == "Sidecar Control" &&
                item.Name == "Validation failed" &&
                item.Value == "1");
        }
        finally
        {
            await server.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task EndpointStateUpdate_AcknowledgesAndUpdatesStatus()
    {
        var port = GetFreeTcpPort();
        var server = CreateServer(
            port,
            new RecordingDirectConnectCodeValidator(MasterNodeKind.Backend),
            requireEndpointReadyBeforeAdvertise: true);
        await server.StartAsync(CancellationToken.None);

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);
            await using var stream = client.GetStream();

            var update = new SidecarEndpointStateUpdate(
                Guid.NewGuid(),
                ready: true,
                "cpp listener ready");
            await WriteEndpointStateUpdateAsync(stream, update);

            var ack = await ReadEndpointStateAckAsync(stream);
            Assert.True(ack.Success);
            Assert.Equal(update.RequestId, ack.RequestId);

            var status = server.GetStatusItems();
            Assert.Contains(status, item =>
                item.Group == "Sidecar Control" &&
                item.Name == "Endpoint readiness required" &&
                item.Value == "Yes");
            Assert.Contains(status, item =>
                item.Group == "Sidecar Control" &&
                item.Name == "Endpoint state" &&
                item.Value == "Ready");
            Assert.Contains(status, item =>
                item.Group == "Sidecar Control" &&
                item.Name == "Endpoint detail" &&
                item.Value.Contains("cpp listener ready", StringComparison.Ordinal));
        }
        finally
        {
            await server.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task RuntimeStatusUpdate_AcknowledgesAndUpdatesStatus()
    {
        var port = GetFreeTcpPort();
        var server = CreateServer(
            port,
            new RecordingDirectConnectCodeValidator(MasterNodeKind.Backend));
        await server.StartAsync(CancellationToken.None);

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);
            await using var stream = client.GetStream();

            var update = new SidecarRuntimeStatusUpdate(
                Guid.NewGuid(),
                healthy: true,
                activeGatewaySessions: 2,
                activeChannels: 7,
                "world tick stable");
            await WriteRuntimeStatusUpdateAsync(stream, update);

            var ack = await ReadRuntimeStatusAckAsync(stream);
            Assert.True(ack.Success);
            Assert.Equal(update.RequestId, ack.RequestId);

            var status = server.GetStatusItems();
            Assert.Contains(status, item =>
                item.Group == "Sidecar Control" &&
                item.Name == "Runtime health" &&
                item.Value == "Healthy");
            Assert.Contains(status, item =>
                item.Group == "Sidecar Control" &&
                item.Name == "Gateway sessions" &&
                item.Value == "2");
            Assert.Contains(status, item =>
                item.Group == "Sidecar Control" &&
                item.Name == "Active channels" &&
                item.Value == "7");
            Assert.Contains(status, item =>
                item.Group == "Sidecar Control" &&
                item.Name == "Runtime detail" &&
                item.Value.Contains("world tick stable", StringComparison.Ordinal));
        }
        finally
        {
            await server.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task ShutdownStateUpdate_AcknowledgesAndMarksEndpointNotReady()
    {
        var port = GetFreeTcpPort();
        var server = CreateServer(
            port,
            new RecordingDirectConnectCodeValidator(MasterNodeKind.Backend),
            requireEndpointReadyBeforeAdvertise: true);
        await server.StartAsync(CancellationToken.None);

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);
            await using var stream = client.GetStream();

            await WriteEndpointStateUpdateAsync(
                stream,
                new SidecarEndpointStateUpdate(
                    Guid.NewGuid(),
                    ready: true,
                    "cpp listener ready"));
            _ = await ReadEndpointStateAckAsync(stream);

            var update = new SidecarShutdownStateUpdate(
                Guid.NewGuid(),
                shuttingDown: true,
                "maintenance window");
            await WriteShutdownStateUpdateAsync(stream, update);

            var ack = await ReadShutdownStateAckAsync(stream);
            Assert.True(ack.Success);
            Assert.Equal(update.RequestId, ack.RequestId);

            var status = server.GetStatusItems();
            Assert.Contains(status, item =>
                item.Group == "Sidecar Control" &&
                item.Name == "Endpoint state" &&
                item.Value == "Not ready");
            Assert.Contains(status, item =>
                item.Group == "Sidecar Control" &&
                item.Name == "Shutdown state" &&
                item.Value == "Requested");
            Assert.Contains(status, item =>
                item.Group == "Sidecar Control" &&
                item.Name == "Shutdown detail" &&
                item.Value.Contains("maintenance window", StringComparison.Ordinal));
        }
        finally
        {
            await server.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task StartAsync_RejectsNonLoopbackEndpoint()
    {
        var server = new SidecarControlServer(
            Microsoft.Extensions.Options.Options.Create(new SidecarControlOptions
            {
                Enabled = true,
                IPAddress = "0.0.0.0",
                Port = GetFreeTcpPort()
            }),
            new RecordingDirectConnectCodeValidator(MasterNodeKind.Backend),
            new TestLogger<SidecarControlServer>());

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await server.StartAsync(CancellationToken.None));
    }

    private static SidecarControlServer CreateServer(
        int port,
        IDirectConnectCodeValidator validator,
        bool requireEndpointReadyBeforeAdvertise = false)
    {
        return new SidecarControlServer(
            Microsoft.Extensions.Options.Options.Create(new SidecarControlOptions
            {
                Enabled = true,
                RequireEndpointReadyBeforeAdvertise = requireEndpointReadyBeforeAdvertise,
                IPAddress = "127.0.0.1",
                Port = port,
                RequestTimeoutMilliseconds = 5000
            }),
            validator,
            new TestLogger<SidecarControlServer>());
    }

    private static async Task WriteValidationRequestAsync(
        Stream stream,
        DirectConnectCodeValidationRequest request)
    {
        using var frame = PacketCodec.Encode(
            PacketKind.Control,
            BackendSidecarControlPacketIds.DirectConnectCodeValidationRequest,
            BackendSidecarControlProtocol.SchemaVersion,
            request,
            DirectConnectCodeValidationRequest.Codec);
        await PacketFrameWriter.WriteAsync(stream, frame, CancellationToken.None);
    }

    private static async Task WriteShutdownStateUpdateAsync(
        Stream stream,
        SidecarShutdownStateUpdate update)
    {
        using var frame = PacketCodec.Encode(
            PacketKind.Control,
            BackendSidecarControlPacketIds.ShutdownStateUpdate,
            BackendSidecarControlProtocol.SchemaVersion,
            update,
            SidecarShutdownStateUpdate.Codec);
        await PacketFrameWriter.WriteAsync(stream, frame, CancellationToken.None);
    }

    private static async Task WriteEndpointStateUpdateAsync(
        Stream stream,
        SidecarEndpointStateUpdate update)
    {
        using var frame = PacketCodec.Encode(
            PacketKind.Control,
            BackendSidecarControlPacketIds.EndpointStateUpdate,
            BackendSidecarControlProtocol.SchemaVersion,
            update,
            SidecarEndpointStateUpdate.Codec);
        await PacketFrameWriter.WriteAsync(stream, frame, CancellationToken.None);
    }

    private static async Task WriteRuntimeStatusUpdateAsync(
        Stream stream,
        SidecarRuntimeStatusUpdate update)
    {
        using var frame = PacketCodec.Encode(
            PacketKind.Control,
            BackendSidecarControlPacketIds.RuntimeStatusUpdate,
            BackendSidecarControlProtocol.SchemaVersion,
            update,
            SidecarRuntimeStatusUpdate.Codec);
        await PacketFrameWriter.WriteAsync(stream, frame, CancellationToken.None);
    }

    private static async Task<DirectConnectCodeValidationResponse> ReadValidationResponseAsync(Stream stream)
    {
        using var frame = await PacketFrameReader.ReadAsync(
            stream,
            BackendSidecarControlProtocol.LocalControlPolicy,
            CancellationToken.None) ?? throw new EndOfStreamException("Sidecar validation response was not written.");
        BackendSidecarControlProtocol.ValidateControlFrame(
            frame,
            BackendSidecarControlPacketIds.DirectConnectCodeValidationResponse);
        return PacketCodec.Decode(frame, DirectConnectCodeValidationResponse.Codec);
    }

    private static async Task<SidecarEndpointStateAck> ReadEndpointStateAckAsync(Stream stream)
    {
        using var frame = await PacketFrameReader.ReadAsync(
            stream,
            BackendSidecarControlProtocol.LocalControlPolicy,
            CancellationToken.None) ?? throw new EndOfStreamException("Sidecar endpoint state ack was not written.");
        BackendSidecarControlProtocol.ValidateControlFrame(
            frame,
            BackendSidecarControlPacketIds.EndpointStateAck);
        return PacketCodec.Decode(frame, SidecarEndpointStateAck.Codec);
    }

    private static async Task<SidecarRuntimeStatusAck> ReadRuntimeStatusAckAsync(Stream stream)
    {
        using var frame = await PacketFrameReader.ReadAsync(
            stream,
            BackendSidecarControlProtocol.LocalControlPolicy,
            CancellationToken.None) ?? throw new EndOfStreamException("Sidecar runtime status ack was not written.");
        BackendSidecarControlProtocol.ValidateControlFrame(
            frame,
            BackendSidecarControlPacketIds.RuntimeStatusAck);
        return PacketCodec.Decode(frame, SidecarRuntimeStatusAck.Codec);
    }

    private static async Task<SidecarShutdownStateAck> ReadShutdownStateAckAsync(Stream stream)
    {
        using var frame = await PacketFrameReader.ReadAsync(
            stream,
            BackendSidecarControlProtocol.LocalControlPolicy,
            CancellationToken.None) ?? throw new EndOfStreamException("Sidecar shutdown state ack was not written.");
        BackendSidecarControlProtocol.ValidateControlFrame(
            frame,
            BackendSidecarControlPacketIds.ShutdownStateAck);
        return PacketCodec.Decode(frame, SidecarShutdownStateAck.Codec);
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
                targetNodeKind == MasterNodeKind.Backend ? "backend-local" : "dedicated-local",
                "backend-master-a",
                string.Empty));
        }
    }

    private sealed record ValidationRequest(
        string Code,
        string GatewayNodeId,
        string GatewayMasterConnectionId);

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
