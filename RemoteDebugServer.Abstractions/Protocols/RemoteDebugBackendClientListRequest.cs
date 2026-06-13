using System;
using PacketCore;

namespace RemoteDebugServer.Protocols;

public sealed class RemoteDebugBackendClientListRequest
{
    public static RemoteDebugBackendClientListRequest Instance { get; } = new RemoteDebugBackendClientListRequest();

    public static IPacketCodec<RemoteDebugBackendClientListRequest> Codec { get; } = new RemoteDebugBackendClientListRequestCodec();

    private sealed class RemoteDebugBackendClientListRequestCodec : IPacketCodec<RemoteDebugBackendClientListRequest>
    {
        public int GetPayloadSize(RemoteDebugBackendClientListRequest value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            return 0;
        }

        public void Encode(RemoteDebugBackendClientListRequest value, ref PacketWriter writer)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }
        }

        public RemoteDebugBackendClientListRequest Decode(ref PacketReader reader)
        {
            return Instance;
        }
    }
}
