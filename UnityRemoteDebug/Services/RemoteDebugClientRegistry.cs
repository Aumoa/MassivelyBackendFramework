using System.Collections.Concurrent;
using UnityRemoteDebug.Contracts;

namespace UnityRemoteDebug.Services;

public sealed class RemoteDebugClientRegistry
{
    private readonly ConcurrentDictionary<Guid, RemoteDebugClientSession> m_Clients = [];
    private readonly object m_Sync = new();

    public event Action? Changed;

    internal bool TryConnect(
        RemoteDebugClientRegistration registration,
        int maxClients,
        int maxClientsPerRemoteEndPoint,
        out RemoteDebugClientSession session)
    {
        session = new RemoteDebugClientSession(
            Guid.NewGuid(),
            Normalize(registration.DisplayName, "Unity Client"),
            Normalize(registration.ProjectName, "Unknown Project"),
            Normalize(registration.UnityVersion, "Unknown"),
            Normalize(registration.Platform, "Unknown"),
            Normalize(registration.RemoteEndPoint, "Unknown"));

        lock (m_Sync)
        {
            if (m_Clients.Count >= maxClients)
            {
                return false;
            }

            var remoteEndPoint = session.RemoteEndPoint;
            var remoteEndPointCount = m_Clients.Values.Count(
                client => string.Equals(client.RemoteEndPoint, remoteEndPoint, StringComparison.Ordinal));
            if (remoteEndPointCount >= maxClientsPerRemoteEndPoint)
            {
                return false;
            }

            m_Clients[session.ClientId] = session;
        }

        OnChanged();
        return true;
    }

    public IReadOnlyList<RemoteDebugClientSnapshot> GetClients()
    {
        return [.. m_Clients.Values
            .Select(static client => client.ToSnapshot())
            .OrderByDescending(static client => client.ConnectedAt)];
    }

    internal void Touch(Guid clientId, TimeSpan notificationInterval)
    {
        if (m_Clients.TryGetValue(clientId, out var client))
        {
            if (client.Touch(notificationInterval))
            {
                OnChanged();
            }
        }
    }

    internal void Disconnect(Guid clientId)
    {
        if (m_Clients.TryRemove(clientId, out _))
        {
            OnChanged();
        }
    }

    private void OnChanged()
    {
        Changed?.Invoke();
    }

    private static string Normalize(string? value, string fallback)
    {
        value = value?.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return value.Length <= 96 ? value : value[..96];
    }
}

internal sealed record RemoteDebugClientRegistration(
    string? DisplayName,
    string? ProjectName,
    string? UnityVersion,
    string? Platform,
    string? RemoteEndPoint);

internal sealed class RemoteDebugClientSession(
    Guid clientId,
    string displayName,
    string projectName,
    string unityVersion,
    string platform,
    string remoteEndPoint)
{
    private readonly object m_Sync = new();
    private DateTimeOffset m_LastSeenAt = DateTimeOffset.UtcNow;
    private DateTimeOffset m_LastNotifiedAt = DateTimeOffset.UtcNow;

    public Guid ClientId { get; } = clientId;

    public string DisplayName { get; } = displayName;

    public string ProjectName { get; } = projectName;

    public string UnityVersion { get; } = unityVersion;

    public string Platform { get; } = platform;

    public string RemoteEndPoint { get; } = remoteEndPoint;

    public DateTimeOffset ConnectedAt { get; } = DateTimeOffset.UtcNow;

    public bool Touch(TimeSpan notificationInterval)
    {
        lock (m_Sync)
        {
            var now = DateTimeOffset.UtcNow;
            m_LastSeenAt = now;
            if (now - m_LastNotifiedAt < notificationInterval)
            {
                return false;
            }

            m_LastNotifiedAt = now;
            return true;
        }
    }

    public RemoteDebugClientSnapshot ToSnapshot()
    {
        lock (m_Sync)
        {
            return new RemoteDebugClientSnapshot(
                ClientId,
                DisplayName,
                ProjectName,
                UnityVersion,
                Platform,
                RemoteEndPoint,
                ConnectedAt,
                m_LastSeenAt);
        }
    }
}
