using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using RemoteDebugServer.Protocols;
using UnityRemoteDebug.Backend.Options;

namespace UnityRemoteDebug.Backend.Services;

internal sealed class RemoteDebugClientRegistry(IOptions<RemoteDebugClientRegistryOptions> options)
{
    private readonly RemoteDebugClientRegistryOptions m_Options = options.Value;
    private readonly ConcurrentDictionary<string, RemoteDebugClientSession> m_Clients = new(StringComparer.Ordinal);

    public int Count => m_Clients.Count;

    public RemoteDebugBackendClientRegisterResponse Register(
        RemoteDebugBackendClientRegisterRequest request,
        DateTimeOffset now)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var registeredAt = now.ToUnixTimeMilliseconds();
        var enabledCapabilities = request.RequestedCapabilities & m_Options.AllowedCapabilities;
        var session = new RemoteDebugClientSession(
            request.ClientId,
            Guid.NewGuid().ToString("N"),
            request.DisplayName,
            request.ClientVersion,
            request.UnityVersion,
            enabledCapabilities,
            registeredAt,
            registeredAt);

        m_Clients.AddOrUpdate(
            request.ClientId,
            session,
            (_, _) => session);

        return new RemoteDebugBackendClientRegisterResponse(
            session.ClientId,
            session.SessionId,
            session.EnabledCapabilities,
            GetHeartbeatIntervalMilliseconds(),
            session.ConnectedAtUnixTimeMilliseconds);
    }

    public bool TryHeartbeat(
        RemoteDebugBackendClientHeartbeatRequest request,
        DateTimeOffset now,
        out RemoteDebugBackendClientHeartbeatResponse? response)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        response = null;
        while (m_Clients.TryGetValue(request.ClientId, out var current))
        {
            if (!string.Equals(current.SessionId, request.SessionId, StringComparison.Ordinal))
            {
                return false;
            }

            var observedAt = now.ToUnixTimeMilliseconds();
            var updated = current.WithLastSeenAt(observedAt);
            if (!m_Clients.TryUpdate(request.ClientId, updated, current))
            {
                continue;
            }

            response = new RemoteDebugBackendClientHeartbeatResponse(
                updated.ClientId,
                updated.SessionId,
                updated.LastSeenAtUnixTimeMilliseconds);
            return true;
        }

        return false;
    }

    public bool Disconnect(RemoteDebugBackendClientDisconnectNotify notify)
    {
        if (notify == null)
        {
            throw new ArgumentNullException(nameof(notify));
        }

        while (m_Clients.TryGetValue(notify.ClientId, out var current))
        {
            if (!string.Equals(current.SessionId, notify.SessionId, StringComparison.Ordinal))
            {
                return false;
            }

            if (((ICollection<KeyValuePair<string, RemoteDebugClientSession>>)m_Clients)
                .Remove(new KeyValuePair<string, RemoteDebugClientSession>(notify.ClientId, current)))
            {
                return true;
            }
        }

        return false;
    }

    public RemoteDebugBackendClientSnapshot[] GetClientSnapshots()
    {
        return [.. m_Clients.Values
            .OrderBy(static client => client.ClientId, StringComparer.Ordinal)
            .Select(static client => new RemoteDebugBackendClientSnapshot(
                client.ClientId,
                client.DisplayName,
                client.ClientVersion,
                client.UnityVersion,
                client.EnabledCapabilities,
                client.ConnectedAtUnixTimeMilliseconds,
                client.LastSeenAtUnixTimeMilliseconds))];
    }

    private int GetHeartbeatIntervalMilliseconds()
    {
        return Math.Max(1, m_Options.HeartbeatIntervalMilliseconds);
    }

    private sealed class RemoteDebugClientSession(
        string clientId,
        string sessionId,
        string displayName,
        string clientVersion,
        string unityVersion,
        RemoteDebugCapabilities enabledCapabilities,
        long connectedAtUnixTimeMilliseconds,
        long lastSeenAtUnixTimeMilliseconds)
    {
        public string ClientId { get; } = clientId;

        public string SessionId { get; } = sessionId;

        public string DisplayName { get; } = displayName;

        public string ClientVersion { get; } = clientVersion;

        public string UnityVersion { get; } = unityVersion;

        public RemoteDebugCapabilities EnabledCapabilities { get; } = enabledCapabilities;

        public long ConnectedAtUnixTimeMilliseconds { get; } = connectedAtUnixTimeMilliseconds;

        public long LastSeenAtUnixTimeMilliseconds { get; } = lastSeenAtUnixTimeMilliseconds;

        public RemoteDebugClientSession WithLastSeenAt(long lastSeenAtUnixTimeMilliseconds)
        {
            return new RemoteDebugClientSession(
                ClientId,
                SessionId,
                DisplayName,
                ClientVersion,
                UnityVersion,
                EnabledCapabilities,
                ConnectedAtUnixTimeMilliseconds,
                lastSeenAtUnixTimeMilliseconds);
        }
    }
}
