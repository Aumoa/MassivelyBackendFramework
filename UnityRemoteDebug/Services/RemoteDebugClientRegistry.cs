using UnityRemoteDebug.Contracts;

namespace UnityRemoteDebug.Services;

public sealed class RemoteDebugClientRegistry
{
    public IReadOnlyList<RemoteDebugClientSnapshot> GetClients()
    {
        return [];
    }
}
