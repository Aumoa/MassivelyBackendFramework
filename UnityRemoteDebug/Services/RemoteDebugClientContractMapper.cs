using RemoteDebugServer.Protocols;
using UnityRemoteDebug.Contracts;

namespace UnityRemoteDebug.Services;

internal static class RemoteDebugClientContractMapper
{
    public static RemoteDebugClientSnapshot ToContract(RemoteDebugBackendClientSnapshot source)
    {
        if (source == null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        return new RemoteDebugClientSnapshot(
            source.ClientId,
            source.DisplayName,
            source.ClientVersion,
            source.UnityVersion,
            FormatCapabilities(source.Capabilities),
            DateTimeOffset.FromUnixTimeMilliseconds(source.ConnectedAtUnixTimeMilliseconds),
            DateTimeOffset.FromUnixTimeMilliseconds(source.LastSeenAtUnixTimeMilliseconds));
    }

    private static string FormatCapabilities(RemoteDebugCapabilities capabilities)
    {
        return capabilities == RemoteDebugCapabilities.None
            ? nameof(RemoteDebugCapabilities.None)
            : capabilities.ToString();
    }
}
