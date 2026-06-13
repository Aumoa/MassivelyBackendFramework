using System;
using PacketCore;

namespace RemoteDebugServer.Protocols;

public sealed class RemoteDebugBackendStatusRequest
{
    public static RemoteDebugBackendStatusRequest Instance { get; } = new RemoteDebugBackendStatusRequest();

    public static IPacketCodec<RemoteDebugBackendStatusRequest> Codec { get; } = new RemoteDebugBackendStatusRequestCodec();

    private sealed class RemoteDebugBackendStatusRequestCodec : IPacketCodec<RemoteDebugBackendStatusRequest>
    {
        public int GetPayloadSize(RemoteDebugBackendStatusRequest value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            return 0;
        }

        public void Encode(RemoteDebugBackendStatusRequest value, ref PacketWriter writer)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }
        }

        public RemoteDebugBackendStatusRequest Decode(ref PacketReader reader)
        {
            return Instance;
        }
    }
}
