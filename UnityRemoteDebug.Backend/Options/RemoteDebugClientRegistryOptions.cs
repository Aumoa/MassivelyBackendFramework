using RemoteDebugServer.Protocols;

namespace UnityRemoteDebug.Backend.Options;

internal sealed class RemoteDebugClientRegistryOptions
{
    public RemoteDebugCapabilities AllowedCapabilities { get; set; } = RemoteDebugCapabilities.LogStreaming;

    public int HeartbeatIntervalMilliseconds { get; set; } = 15000;
}
