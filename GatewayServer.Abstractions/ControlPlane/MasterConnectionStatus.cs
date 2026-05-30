using System;

namespace GatewayServer.ControlPlane;

public sealed class MasterConnectionStatus
{
    public MasterConnectionStatus(
        MasterConnectionState state,
        bool isEnabled,
        bool isTrusted,
        string endpoint,
        string nodeId,
        string displayName,
        string? masterConnectionId,
        string? handshakeStep,
        string? lastError,
        DateTimeOffset lastChangedAt,
        DateTimeOffset? lastConnectedAt,
        DateTimeOffset? lastTrustedAt,
        DateTimeOffset? nextReconnectAt)
    {
        State = state;
        IsEnabled = isEnabled;
        IsTrusted = isTrusted;
        Endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
        NodeId = nodeId ?? throw new ArgumentNullException(nameof(nodeId));
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
        MasterConnectionId = masterConnectionId;
        HandshakeStep = handshakeStep;
        LastError = lastError;
        LastChangedAt = lastChangedAt;
        LastConnectedAt = lastConnectedAt;
        LastTrustedAt = lastTrustedAt;
        NextReconnectAt = nextReconnectAt;
    }

    public MasterConnectionState State { get; }

    public bool IsEnabled { get; }

    public bool IsTrusted { get; }

    public string Endpoint { get; }

    public string NodeId { get; }

    public string DisplayName { get; }

    public string? MasterConnectionId { get; }

    public string? HandshakeStep { get; }

    public string? LastError { get; }

    public DateTimeOffset LastChangedAt { get; }

    public DateTimeOffset? LastConnectedAt { get; }

    public DateTimeOffset? LastTrustedAt { get; }

    public DateTimeOffset? NextReconnectAt { get; }
}
