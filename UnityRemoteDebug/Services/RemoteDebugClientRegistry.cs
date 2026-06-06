using System.Collections.Concurrent;
using UnityRemoteDebug.Contracts;

namespace UnityRemoteDebug.Services;

public sealed class RemoteDebugClientRegistry
{
    private readonly ConcurrentDictionary<Guid, RemoteDebugClientSession> m_Clients = [];

    public event Action? Changed;

    internal RemoteDebugClientSession Connect(RemoteDebugClientRegistration registration)
    {
        var client = new RemoteDebugClientSession(
            Guid.NewGuid(),
            Normalize(registration.DisplayName, "Unity Client"),
            Normalize(registration.ProjectName, "Unknown Project"),
            Normalize(registration.UnityVersion, "Unknown"),
            Normalize(registration.Platform, "Unknown"),
            Normalize(registration.RemoteEndPoint, "Unknown"));

        m_Clients[client.ClientId] = client;
        OnChanged();
        return client;
    }

    public IReadOnlyList<RemoteDebugClientSnapshot> GetClients()
    {
        return [.. m_Clients.Values
            .Select(static client => client.ToSnapshot())
            .OrderByDescending(static client => client.ConnectedAt)];
    }

    internal void Touch(Guid clientId)
    {
        if (m_Clients.TryGetValue(clientId, out var client))
        {
            client.Touch();
            OnChanged();
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

    public Guid ClientId { get; } = clientId;

    public string DisplayName { get; } = displayName;

    public string ProjectName { get; } = projectName;

    public string UnityVersion { get; } = unityVersion;

    public string Platform { get; } = platform;

    public string RemoteEndPoint { get; } = remoteEndPoint;

    public DateTimeOffset ConnectedAt { get; } = DateTimeOffset.UtcNow;

    public void Touch()
    {
        lock (m_Sync)
        {
            m_LastSeenAt = DateTimeOffset.UtcNow;
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
