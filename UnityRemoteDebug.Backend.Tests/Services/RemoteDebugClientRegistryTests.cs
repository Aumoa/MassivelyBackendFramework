using RemoteDebugServer.Protocols;
using UnityRemoteDebug.Backend.Options;
using UnityRemoteDebug.Backend.Services;
using Xunit;

namespace UnityRemoteDebug.Backend.Tests.Services;

public sealed class RemoteDebugClientRegistryTests
{
    [Fact]
    public void Register_EnablesOnlyServerAllowedCapabilities()
    {
        var registry = CreateRegistry(RemoteDebugCapabilities.LogStreaming);

        var response = registry.Register(
            new RemoteDebugBackendClientRegisterRequest(
                "client-a",
                "Editor",
                "1.2.3",
                "6000.0.1f1",
                RemoteDebugCapabilities.LogStreaming | RemoteDebugCapabilities.RemoteControl),
            DateTimeOffset.FromUnixTimeMilliseconds(1000));

        Assert.Equal(RemoteDebugCapabilities.LogStreaming, response.EnabledCapabilities);
        Assert.Equal(15000, response.HeartbeatIntervalMilliseconds);
        Assert.Equal(1000, response.RegisteredAtUnixTimeMilliseconds);
        Assert.Equal(1, registry.Count);

        var client = Assert.Single(registry.GetClientSnapshots());
        Assert.Equal("client-a", client.ClientId);
        Assert.Equal("Editor", client.DisplayName);
        Assert.Equal("1.2.3", client.ClientVersion);
        Assert.Equal("6000.0.1f1", client.UnityVersion);
        Assert.Equal(RemoteDebugCapabilities.LogStreaming, client.Capabilities);
        Assert.Equal(1000, client.ConnectedAtUnixTimeMilliseconds);
        Assert.Equal(1000, client.LastSeenAtUnixTimeMilliseconds);
    }

    [Fact]
    public void Register_ReplacesExistingClientSession()
    {
        var registry = CreateRegistry(RemoteDebugCapabilities.LogStreaming | RemoteDebugCapabilities.FileTransfer);
        var first = registry.Register(
            new RemoteDebugBackendClientRegisterRequest(
                "client-a",
                "Editor",
                "1.0.0",
                "2022.3",
                RemoteDebugCapabilities.LogStreaming),
            DateTimeOffset.FromUnixTimeMilliseconds(1000));

        var second = registry.Register(
            new RemoteDebugBackendClientRegisterRequest(
                "client-a",
                "Editor New",
                "2.0.0",
                "6000.0.1f1",
                RemoteDebugCapabilities.FileTransfer),
            DateTimeOffset.FromUnixTimeMilliseconds(2000));

        Assert.NotEqual(first.SessionId, second.SessionId);
        Assert.False(registry.TryHeartbeat(
            new RemoteDebugBackendClientHeartbeatRequest("client-a", first.SessionId),
            DateTimeOffset.FromUnixTimeMilliseconds(3000),
            out _));

        var client = Assert.Single(registry.GetClientSnapshots());
        Assert.Equal("Editor New", client.DisplayName);
        Assert.Equal("2.0.0", client.ClientVersion);
        Assert.Equal(RemoteDebugCapabilities.FileTransfer, client.Capabilities);
        Assert.Equal(2000, client.ConnectedAtUnixTimeMilliseconds);
    }

    [Fact]
    public void Heartbeat_UpdatesLastSeenForMatchingSession()
    {
        var registry = CreateRegistry(RemoteDebugCapabilities.LogStreaming);
        var registered = registry.Register(
            new RemoteDebugBackendClientRegisterRequest(
                "client-a",
                "Editor",
                "1.0.0",
                "2022.3",
                RemoteDebugCapabilities.LogStreaming),
            DateTimeOffset.FromUnixTimeMilliseconds(1000));

        var success = registry.TryHeartbeat(
            new RemoteDebugBackendClientHeartbeatRequest("client-a", registered.SessionId),
            DateTimeOffset.FromUnixTimeMilliseconds(5000),
            out var response);

        Assert.True(success);
        Assert.NotNull(response);
        Assert.Equal("client-a", response.ClientId);
        Assert.Equal(registered.SessionId, response.SessionId);
        Assert.Equal(5000, response.ObservedAtUnixTimeMilliseconds);
        Assert.Equal(5000, Assert.Single(registry.GetClientSnapshots()).LastSeenAtUnixTimeMilliseconds);
    }

    [Fact]
    public void Disconnect_RemovesOnlyMatchingSession()
    {
        var registry = CreateRegistry(RemoteDebugCapabilities.LogStreaming);
        var registered = registry.Register(
            new RemoteDebugBackendClientRegisterRequest(
                "client-a",
                "Editor",
                "1.0.0",
                "2022.3",
                RemoteDebugCapabilities.LogStreaming),
            DateTimeOffset.FromUnixTimeMilliseconds(1000));

        Assert.False(registry.Disconnect(new RemoteDebugBackendClientDisconnectNotify(
            "client-a",
            "wrong-session",
            "stale")));
        Assert.Equal(1, registry.Count);

        Assert.True(registry.Disconnect(new RemoteDebugBackendClientDisconnectNotify(
            "client-a",
            registered.SessionId,
            "shutdown")));
        Assert.Equal(0, registry.Count);
        Assert.Empty(registry.GetClientSnapshots());
    }

    [Fact]
    public void PruneExpired_RemovesClientsAtTimeoutBoundary()
    {
        var registry = CreateRegistry(
            RemoteDebugCapabilities.LogStreaming,
            heartbeatIntervalMilliseconds: 1000,
            clientTimeoutMilliseconds: 3000);
        registry.Register(
            new RemoteDebugBackendClientRegisterRequest(
                "client-a",
                "Editor",
                "1.0.0",
                "2022.3",
                RemoteDebugCapabilities.LogStreaming),
            DateTimeOffset.FromUnixTimeMilliseconds(1000));

        Assert.Equal(0, registry.PruneExpired(DateTimeOffset.FromUnixTimeMilliseconds(3999)));
        Assert.Equal(1, registry.Count);

        Assert.Equal(1, registry.PruneExpired(DateTimeOffset.FromUnixTimeMilliseconds(4000)));
        Assert.Equal(0, registry.Count);
        Assert.Empty(registry.GetClientSnapshots());
    }

    [Fact]
    public void Heartbeat_RejectsAndRemovesExpiredClientSession()
    {
        var registry = CreateRegistry(
            RemoteDebugCapabilities.LogStreaming,
            heartbeatIntervalMilliseconds: 1000,
            clientTimeoutMilliseconds: 3000);
        var registered = registry.Register(
            new RemoteDebugBackendClientRegisterRequest(
                "client-a",
                "Editor",
                "1.0.0",
                "2022.3",
                RemoteDebugCapabilities.LogStreaming),
            DateTimeOffset.FromUnixTimeMilliseconds(1000));

        var success = registry.TryHeartbeat(
            new RemoteDebugBackendClientHeartbeatRequest("client-a", registered.SessionId),
            DateTimeOffset.FromUnixTimeMilliseconds(4000),
            out var response);

        Assert.False(success);
        Assert.Null(response);
        Assert.Equal(0, registry.Count);
    }

    [Fact]
    public void SnapshotAndCountQueries_PruneExpiredClients()
    {
        var registry = CreateRegistry(
            RemoteDebugCapabilities.LogStreaming,
            heartbeatIntervalMilliseconds: 1000,
            clientTimeoutMilliseconds: 3000);
        registry.Register(
            new RemoteDebugBackendClientRegisterRequest(
                "client-a",
                "Editor",
                "1.0.0",
                "2022.3",
                RemoteDebugCapabilities.LogStreaming),
            DateTimeOffset.FromUnixTimeMilliseconds(1000));

        Assert.Equal(1, registry.GetClientCount(DateTimeOffset.FromUnixTimeMilliseconds(3999)));
        Assert.Empty(registry.GetClientSnapshots(DateTimeOffset.FromUnixTimeMilliseconds(4000)));
        Assert.Equal(0, registry.Count);
    }

    private static RemoteDebugClientRegistry CreateRegistry(
        RemoteDebugCapabilities allowedCapabilities,
        int heartbeatIntervalMilliseconds = 15000,
        int clientTimeoutMilliseconds = 45000)
    {
        return new RemoteDebugClientRegistry(
            Microsoft.Extensions.Options.Options.Create(new RemoteDebugClientRegistryOptions
            {
                AllowedCapabilities = allowedCapabilities,
                HeartbeatIntervalMilliseconds = heartbeatIntervalMilliseconds,
                ClientTimeoutMilliseconds = clientTimeoutMilliseconds
            }));
    }
}
