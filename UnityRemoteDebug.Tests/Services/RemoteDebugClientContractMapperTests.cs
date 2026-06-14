using RemoteDebugServer.Protocols;
using UnityRemoteDebug.Services;
using Xunit;

namespace UnityRemoteDebug.Tests.Services;

public sealed class RemoteDebugClientContractMapperTests
{
    [Fact]
    public void ToContract_MapsBackendSnapshotToFrontendSnapshot()
    {
        var source = new RemoteDebugBackendClientSnapshot(
            "client-a",
            "Editor",
            "1.2.3",
            "6000.0.1f1",
            RemoteDebugCapabilities.LogStreaming | RemoteDebugCapabilities.FileTransfer,
            connectedAtUnixTimeMilliseconds: 1000,
            lastSeenAtUnixTimeMilliseconds: 2000);

        var mapped = RemoteDebugClientContractMapper.ToContract(source);

        Assert.Equal("client-a", mapped.ClientId);
        Assert.Equal("Editor", mapped.DisplayName);
        Assert.Equal("1.2.3", mapped.ClientVersion);
        Assert.Equal("6000.0.1f1", mapped.UnityVersion);
        Assert.Equal("LogStreaming, FileTransfer", mapped.Capabilities);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1000), mapped.ConnectedAt);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(2000), mapped.LastSeenAt);
    }

    [Fact]
    public void ToContract_FormatsEmptyCapabilitiesAsNone()
    {
        var source = new RemoteDebugBackendClientSnapshot(
            "client-a",
            "Editor",
            "1.2.3",
            "6000.0.1f1",
            RemoteDebugCapabilities.None,
            connectedAtUnixTimeMilliseconds: 1000,
            lastSeenAtUnixTimeMilliseconds: 2000);

        var mapped = RemoteDebugClientContractMapper.ToContract(source);

        Assert.Equal("None", mapped.Capabilities);
    }
}
