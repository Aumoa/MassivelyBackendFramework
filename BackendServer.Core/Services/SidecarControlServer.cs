using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using BackendServer.Options;
using MasterServer.ControlPlane;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PacketCore;

namespace BackendServer.Services;

internal interface ISidecarControlStatusProvider
{
    ServiceAdminStatusItem[] GetStatusItems();
}

internal interface IBackendEndpointReadiness
{
    bool RequiresEndpointReadyBeforeAdvertise { get; }

    Task WaitUntilReadyForAdvertisementAsync(CancellationToken cancellationToken);
}

internal static class BackendSidecarControlPacketIds
{
    public const ushort DirectConnectCodeValidationRequest = 1;
    public const ushort DirectConnectCodeValidationResponse = 2;
    public const ushort EndpointStateUpdate = 3;
    public const ushort EndpointStateAck = 4;
    public const ushort RuntimeStatusUpdate = 5;
    public const ushort RuntimeStatusAck = 6;
    public const ushort ShutdownStateUpdate = 7;
    public const ushort ShutdownStateAck = 8;
}

internal static class BackendSidecarControlProtocol
{
    public const ushort SchemaVersion = 1;
    public const int MaxPayloadLength = 16 * 1024;

    public static readonly PacketReadPolicy LocalControlPolicy = new(
        PacketKindMask.Control,
        MaxPayloadLength,
        rejectUnknownFlags: true);

    public static void ValidateControlFrame(PacketFrame frame, ushort expectedPacketId)
    {
        var header = frame.Header;
        if (header.Kind != PacketKind.Control ||
            header.Flags != PacketFlags.None ||
            header.PacketId != expectedPacketId ||
            header.Version != SchemaVersion)
        {
            throw new InvalidOperationException(
                $"Unexpected Backend sidecar control packet. Expected id {expectedPacketId}, received kind {header.Kind}, id {header.PacketId}, version {header.Version}.");
        }
    }
}

internal sealed class SidecarControlServer(
    IOptions<SidecarControlOptions> options,
    IDirectConnectCodeValidator directConnectCodeValidator,
    ILogger<SidecarControlServer> logger) : IHostedService, ISidecarControlStatusProvider, IBackendEndpointReadiness
{
    private readonly SidecarControlOptions m_Options = options.Value;
    private readonly CancellationTokenSource m_Shutdown = new();
    private readonly object m_EndpointReadinessSync = new();
    private readonly object m_RuntimeStatusSync = new();
    private readonly object m_ShutdownStateSync = new();
    private readonly ConcurrentDictionary<Guid, Task> m_ConnectionTasks = [];
    private readonly ConcurrentDictionary<Guid, string> m_ConnectionStates = [];
    private TaskCompletionSource<bool> m_EndpointReadySignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool m_EndpointReady;
    private string m_EndpointReadinessDetail = "Not signaled";
    private DateTimeOffset? m_EndpointReadinessChangedAt;
    private bool? m_RuntimeHealthy;
    private int? m_RuntimeActiveGatewaySessions;
    private int? m_RuntimeActiveChannels;
    private string m_RuntimeStatusDetail = "Not reported";
    private DateTimeOffset? m_RuntimeStatusChangedAt;
    private bool m_CppRuntimeShuttingDown;
    private string m_CppRuntimeShutdownReason = "Not requested";
    private DateTimeOffset? m_CppRuntimeShutdownChangedAt;
    private long m_ValidationRequestCount;
    private long m_ValidationSuccessCount;
    private long m_ValidationFailureCount;
    private Socket? m_Socket;
    private Task? m_AcceptTask;

    public bool RequiresEndpointReadyBeforeAdvertise => m_Options.Enabled && m_Options.RequireEndpointReadyBeforeAdvertise;

    public Task WaitUntilReadyForAdvertisementAsync(CancellationToken cancellationToken)
    {
        if (!RequiresEndpointReadyBeforeAdvertise)
        {
            return Task.CompletedTask;
        }

        Task readyTask;
        lock (m_EndpointReadinessSync)
        {
            if (m_EndpointReady)
            {
                return Task.CompletedTask;
            }

            readyTask = m_EndpointReadySignal.Task;
        }

        return readyTask.WaitAsync(cancellationToken);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!m_Options.Enabled)
        {
            logger.LogInformation("Backend sidecar control listener is disabled.");
            return;
        }

        EnsureConfigured();

        var listenAddress = await MasterEndpointResolver.ResolveBindAddressAsync(
            m_Options.IPAddress,
            cancellationToken).ConfigureAwait(false);
        if (!IPAddress.IsLoopback(listenAddress))
        {
            throw new InvalidOperationException("SidecarControl:IPAddress must resolve to a loopback address.");
        }

        m_Socket = new Socket(listenAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        m_Socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, true);
        m_Socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        m_Socket.Bind(new IPEndPoint(listenAddress, m_Options.Port));
        m_Socket.Listen(m_Options.Backlog);

        logger.LogInformation(
            "Backend sidecar control listener is running on {Address}:{Port}.",
            m_Options.IPAddress,
            m_Options.Port);
        m_AcceptTask = AcceptLoopAsync(m_Shutdown.Token);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await m_Shutdown.CancelAsync().ConfigureAwait(false);
        m_Socket?.Dispose();

        if (m_AcceptTask != null)
        {
            await WaitForShutdownAsync(m_AcceptTask, cancellationToken).ConfigureAwait(false);
        }

        var connectionTasks = m_ConnectionTasks.Values.ToArray();
        if (connectionTasks.Length > 0)
        {
            await WaitForShutdownAsync(Task.WhenAll(connectionTasks), cancellationToken).ConfigureAwait(false);
        }
    }

    public ServiceAdminStatusItem[] GetStatusItems()
    {
        if (!m_Options.Enabled)
        {
            return
            [
                new("Sidecar Control", "Listener", "Disabled")
            ];
        }

        return
        [
            new("Sidecar Control", "Listener", $"{m_Options.IPAddress}:{m_Options.Port}"),
            new("Sidecar Control", "Endpoint readiness required", m_Options.RequireEndpointReadyBeforeAdvertise ? "Yes" : "No"),
            new("Sidecar Control", "Endpoint state", GetEndpointStateStatus()),
            new("Sidecar Control", "Endpoint detail", GetEndpointReadinessDetail()),
            new("Sidecar Control", "Runtime health", GetRuntimeHealthStatus()),
            new("Sidecar Control", "Gateway sessions", GetRuntimeActiveGatewaySessionsStatus()),
            new("Sidecar Control", "Active channels", GetRuntimeActiveChannelsStatus()),
            new("Sidecar Control", "Runtime detail", GetRuntimeStatusDetail()),
            new("Sidecar Control", "Shutdown state", GetShutdownStateStatus()),
            new("Sidecar Control", "Shutdown detail", GetShutdownDetail()),
            new("Sidecar Control", "Active connections", m_ConnectionStates.Count.ToString()),
            new("Sidecar Control", "Validation requests", Interlocked.Read(ref m_ValidationRequestCount).ToString()),
            new("Sidecar Control", "Validation succeeded", Interlocked.Read(ref m_ValidationSuccessCount).ToString()),
            new("Sidecar Control", "Validation failed", Interlocked.Read(ref m_ValidationFailureCount).ToString())
        ];
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            Socket? socket = null;

            try
            {
                socket = await m_Socket!.AcceptAsync(cancellationToken).ConfigureAwait(false);
                socket.NoDelay = true;
                StartConnection(socket, cancellationToken);
                socket = null;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                socket?.Dispose();
                return;
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                socket?.Dispose();
                return;
            }
            catch (Exception e)
            {
                socket?.Dispose();
                logger.LogError(e, "Error occurred while accepting a Backend sidecar control connection.");
            }
        }
    }

    private void StartConnection(Socket socket, CancellationToken cancellationToken)
    {
        var connectionId = Guid.NewGuid();
        var task = HandleConnectionAsync(connectionId, socket, cancellationToken);
        m_ConnectionTasks.TryAdd(connectionId, task);

        _ = task.ContinueWith(
            completed =>
            {
                m_ConnectionTasks.TryRemove(connectionId, out _);
                m_ConnectionStates.TryRemove(connectionId, out _);
                socket.Dispose();

                if (completed.Exception != null)
                {
                    logger.LogError(completed.Exception, "Unhandled Backend sidecar control connection task failure.");
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task HandleConnectionAsync(
        Guid connectionId,
        Socket socket,
        CancellationToken cancellationToken)
    {
        await using var stream = new NetworkStream(socket, ownsSocket: false);
        m_ConnectionStates[connectionId] = "Connected";

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var frame = await PacketFrameReader.ReadAsync(
                    stream,
                    BackendSidecarControlProtocol.LocalControlPolicy,
                    cancellationToken).ConfigureAwait(false);

                if (frame == null)
                {
                    return;
                }

                using (frame)
                {
                    if (frame.Header.PacketId == BackendSidecarControlPacketIds.DirectConnectCodeValidationRequest)
                    {
                        await HandleDirectConnectCodeValidationAsync(
                            stream,
                            frame,
                            cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    if (frame.Header.PacketId == BackendSidecarControlPacketIds.EndpointStateUpdate)
                    {
                        await HandleEndpointStateUpdateAsync(
                            stream,
                            frame,
                            cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    if (frame.Header.PacketId == BackendSidecarControlPacketIds.RuntimeStatusUpdate)
                    {
                        await HandleRuntimeStatusUpdateAsync(
                            stream,
                            frame,
                            cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    if (frame.Header.PacketId == BackendSidecarControlPacketIds.ShutdownStateUpdate)
                    {
                        await HandleShutdownStateUpdateAsync(
                            stream,
                            frame,
                            cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    logger.LogWarning(
                        "Backend sidecar control rejected unsupported packet. ConnectionId={ConnectionId}, PacketKind={PacketKind}, PacketId={PacketId}.",
                        connectionId,
                        frame.Header.Kind,
                        frame.Header.PacketId);
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception e) when (IsRemoteDisconnect(e))
        {
            m_ConnectionStates[connectionId] = "Disconnected";
            logger.LogDebug(e, "Backend sidecar control connection closed by the remote peer.");
        }
        catch (Exception e)
        {
            m_ConnectionStates[connectionId] = "Error";
            logger.LogWarning(e, "Backend sidecar control connection ended with an unexpected error.");
        }
    }

    private async ValueTask HandleDirectConnectCodeValidationAsync(
        Stream stream,
        PacketFrame frame,
        CancellationToken cancellationToken)
    {
        BackendSidecarControlProtocol.ValidateControlFrame(
            frame,
            BackendSidecarControlPacketIds.DirectConnectCodeValidationRequest);
        var request = PacketCodec.Decode(frame, DirectConnectCodeValidationRequest.Codec);
        Interlocked.Increment(ref m_ValidationRequestCount);

        var response = await ValidateDirectConnectCodeAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.Success)
        {
            Interlocked.Increment(ref m_ValidationSuccessCount);
        }
        else
        {
            Interlocked.Increment(ref m_ValidationFailureCount);
        }

        using var responseFrame = PacketCodec.Encode(
            PacketKind.Control,
            BackendSidecarControlPacketIds.DirectConnectCodeValidationResponse,
            BackendSidecarControlProtocol.SchemaVersion,
            response,
            DirectConnectCodeValidationResponse.Codec);
        await PacketFrameWriter.WriteAsync(stream, responseFrame, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask HandleShutdownStateUpdateAsync(
        Stream stream,
        PacketFrame frame,
        CancellationToken cancellationToken)
    {
        BackendSidecarControlProtocol.ValidateControlFrame(
            frame,
            BackendSidecarControlPacketIds.ShutdownStateUpdate);
        var update = PacketCodec.Decode(frame, SidecarShutdownStateUpdate.Codec);
        SetShutdownState(update.ShuttingDown, update.Reason);

        using var responseFrame = PacketCodec.Encode(
            PacketKind.Control,
            BackendSidecarControlPacketIds.ShutdownStateAck,
            BackendSidecarControlProtocol.SchemaVersion,
            SidecarShutdownStateAck.SuccessResult(update.RequestId),
            SidecarShutdownStateAck.Codec);
        await PacketFrameWriter.WriteAsync(stream, responseFrame, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask HandleEndpointStateUpdateAsync(
        Stream stream,
        PacketFrame frame,
        CancellationToken cancellationToken)
    {
        BackendSidecarControlProtocol.ValidateControlFrame(
            frame,
            BackendSidecarControlPacketIds.EndpointStateUpdate);
        var update = PacketCodec.Decode(frame, SidecarEndpointStateUpdate.Codec);
        SetEndpointReadiness(update.Ready, update.Detail);

        using var responseFrame = PacketCodec.Encode(
            PacketKind.Control,
            BackendSidecarControlPacketIds.EndpointStateAck,
            BackendSidecarControlProtocol.SchemaVersion,
            SidecarEndpointStateAck.SuccessResult(update.RequestId),
            SidecarEndpointStateAck.Codec);
        await PacketFrameWriter.WriteAsync(stream, responseFrame, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask HandleRuntimeStatusUpdateAsync(
        Stream stream,
        PacketFrame frame,
        CancellationToken cancellationToken)
    {
        BackendSidecarControlProtocol.ValidateControlFrame(
            frame,
            BackendSidecarControlPacketIds.RuntimeStatusUpdate);
        var update = PacketCodec.Decode(frame, SidecarRuntimeStatusUpdate.Codec);
        SetRuntimeStatus(update);

        using var responseFrame = PacketCodec.Encode(
            PacketKind.Control,
            BackendSidecarControlPacketIds.RuntimeStatusAck,
            BackendSidecarControlProtocol.SchemaVersion,
            SidecarRuntimeStatusAck.SuccessResult(update.RequestId),
            SidecarRuntimeStatusAck.Codec);
        await PacketFrameWriter.WriteAsync(stream, responseFrame, cancellationToken).ConfigureAwait(false);
    }

    private async Task<DirectConnectCodeValidationResponse> ValidateDirectConnectCodeAsync(
        DirectConnectCodeValidationRequest request,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(Math.Max(1, m_Options.RequestTimeoutMilliseconds)));

        try
        {
            var validation = await directConnectCodeValidator.ValidateDirectConnectCodeAsync(
                request.Code,
                request.GatewayNodeId,
                request.GatewayMasterConnectionId,
                timeout.Token).ConfigureAwait(false);
            return CreateLocalResponse(request, validation);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            return DirectConnectCodeValidationResponse.Failure(
                request.RequestId,
                $"Timed out validating direct-connect code after {Math.Max(1, m_Options.RequestTimeoutMilliseconds)} ms.");
        }
        catch (Exception e) when (e is UnauthorizedAccessException or InvalidOperationException or IOException)
        {
            return DirectConnectCodeValidationResponse.Failure(request.RequestId, e.Message);
        }
    }

    private static DirectConnectCodeValidationResponse CreateLocalResponse(
        DirectConnectCodeValidationRequest request,
        DirectConnectCodeValidationResponse validation)
    {
        if (!validation.Success)
        {
            return DirectConnectCodeValidationResponse.Failure(request.RequestId, validation.ErrorMessage);
        }

        if (!string.Equals(validation.GatewayNodeId, request.GatewayNodeId, StringComparison.Ordinal) ||
            !string.Equals(validation.GatewayMasterConnectionId, request.GatewayMasterConnectionId, StringComparison.Ordinal) ||
            validation.TargetNodeKind != MasterNodeKind.Backend)
        {
            return DirectConnectCodeValidationResponse.Failure(
                request.RequestId,
                "Direct connect code validation returned an unexpected connection identity.");
        }

        return new DirectConnectCodeValidationResponse(
            request.RequestId,
            success: true,
            validation.GatewayNodeId,
            validation.GatewayMasterConnectionId,
            validation.TargetNodeKind,
            validation.TargetNodeId,
            validation.TargetMasterConnectionId,
            string.Empty);
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(m_Options.IPAddress))
        {
            throw new InvalidOperationException("SidecarControl:IPAddress must be configured.");
        }

        if (m_Options.Port <= 0 || m_Options.Port > 65535)
        {
            throw new InvalidOperationException("SidecarControl:Port must be between 1 and 65535.");
        }

        if (m_Options.Backlog <= 0)
        {
            throw new InvalidOperationException("SidecarControl:Backlog must be greater than zero.");
        }
    }

    private void SetEndpointReadiness(bool ready, string detail)
    {
        TaskCompletionSource<bool>? readySignal = null;

        lock (m_EndpointReadinessSync)
        {
            m_EndpointReady = ready;
            m_EndpointReadinessDetail = string.IsNullOrWhiteSpace(detail) ? (ready ? "Ready" : "Not ready") : detail;
            m_EndpointReadinessChangedAt = DateTimeOffset.UtcNow;

            if (ready)
            {
                readySignal = m_EndpointReadySignal;
            }
            else if (m_EndpointReadySignal.Task.IsCompleted)
            {
                m_EndpointReadySignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        readySignal?.TrySetResult(true);
    }

    private void SetRuntimeStatus(SidecarRuntimeStatusUpdate update)
    {
        lock (m_RuntimeStatusSync)
        {
            m_RuntimeHealthy = update.Healthy;
            m_RuntimeActiveGatewaySessions = update.ActiveGatewaySessions;
            m_RuntimeActiveChannels = update.ActiveChannels;
            m_RuntimeStatusDetail = string.IsNullOrWhiteSpace(update.Detail)
                ? (update.Healthy ? "Healthy" : "Unhealthy")
                : update.Detail;
            m_RuntimeStatusChangedAt = DateTimeOffset.UtcNow;
        }
    }

    private string GetEndpointStateStatus()
    {
        lock (m_EndpointReadinessSync)
        {
            return m_EndpointReady ? "Ready" : "Not ready";
        }
    }

    private string GetEndpointReadinessDetail()
    {
        lock (m_EndpointReadinessSync)
        {
            return m_EndpointReadinessChangedAt.HasValue
                ? $"{m_EndpointReadinessDetail} ({m_EndpointReadinessChangedAt.Value.LocalDateTime:O})"
                : m_EndpointReadinessDetail;
        }
    }

    private void SetShutdownState(bool shuttingDown, string reason)
    {
        var detail = string.IsNullOrWhiteSpace(reason)
            ? (shuttingDown ? "Graceful shutdown requested" : "Shutdown cleared")
            : reason;

        lock (m_ShutdownStateSync)
        {
            m_CppRuntimeShuttingDown = shuttingDown;
            m_CppRuntimeShutdownReason = detail;
            m_CppRuntimeShutdownChangedAt = DateTimeOffset.UtcNow;
        }

        if (shuttingDown)
        {
            SetEndpointReadiness(ready: false, detail);
        }
    }

    private string GetRuntimeHealthStatus()
    {
        lock (m_RuntimeStatusSync)
        {
            return m_RuntimeHealthy.HasValue
                ? (m_RuntimeHealthy.Value ? "Healthy" : "Unhealthy")
                : "Unknown";
        }
    }

    private string GetRuntimeActiveGatewaySessionsStatus()
    {
        lock (m_RuntimeStatusSync)
        {
            return m_RuntimeActiveGatewaySessions?.ToString() ?? "Unknown";
        }
    }

    private string GetRuntimeActiveChannelsStatus()
    {
        lock (m_RuntimeStatusSync)
        {
            return m_RuntimeActiveChannels?.ToString() ?? "Unknown";
        }
    }

    private string GetRuntimeStatusDetail()
    {
        lock (m_RuntimeStatusSync)
        {
            return m_RuntimeStatusChangedAt.HasValue
                ? $"{m_RuntimeStatusDetail} ({m_RuntimeStatusChangedAt.Value.LocalDateTime:O})"
                : m_RuntimeStatusDetail;
        }
    }

    private string GetShutdownStateStatus()
    {
        lock (m_ShutdownStateSync)
        {
            return m_CppRuntimeShuttingDown ? "Requested" : "Not requested";
        }
    }

    private string GetShutdownDetail()
    {
        lock (m_ShutdownStateSync)
        {
            return m_CppRuntimeShutdownChangedAt.HasValue
                ? $"{m_CppRuntimeShutdownReason} ({m_CppRuntimeShutdownChangedAt.Value.LocalDateTime:O})"
                : m_CppRuntimeShutdownReason;
        }
    }

    private static bool IsRemoteDisconnect(Exception exception)
    {
        return exception is EndOfStreamException ||
               exception is IOException { InnerException: SocketException innerSocketException } && IsRemoteDisconnect(innerSocketException) ||
               exception is SocketException socketException && IsRemoteDisconnect(socketException);
    }

    private static bool IsRemoteDisconnect(SocketException exception)
    {
        return exception.SocketErrorCode is SocketError.ConnectionReset or SocketError.ConnectionAborted or SocketError.Shutdown;
    }

    private static async Task WaitForShutdownAsync(Task task, CancellationToken cancellationToken)
    {
        try
        {
            await task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
