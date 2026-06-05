using RemoteDebugServer.Protocols;

namespace RemoteDebug;

public sealed class RemoteDebugSessionInfo
{
    public RemoteDebugSessionInfo(
        string clientId,
        string sessionId,
        RemoteDebugCapabilities enabledCapabilities,
        int heartbeatIntervalMilliseconds)
    {
        ClientId = clientId;
        SessionId = sessionId;
        EnabledCapabilities = enabledCapabilities;
        HeartbeatIntervalMilliseconds = heartbeatIntervalMilliseconds;
    }

    public string ClientId { get; }

    public string SessionId { get; }

    public RemoteDebugCapabilities EnabledCapabilities { get; }

    public int HeartbeatIntervalMilliseconds { get; }
}
