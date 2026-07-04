using System;
using PacketCore;

namespace GatewayServer.Protocols;

public sealed class GatewayBackendServerListRequest
{
    public const ushort ProtocolVersion = 1;
    public const int MaxRequestedEntries = 1024;

    public GatewayBackendServerListRequest(string backendKind, int maximumEntries = 0)
    {
        if (string.IsNullOrWhiteSpace(backendKind))
        {
            throw new ArgumentException("Backend kind is required.", nameof(backendKind));
        }

        if (maximumEntries < 0 || maximumEntries > MaxRequestedEntries)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumEntries));
        }

        BackendKind = backendKind.Trim();
        MaximumEntries = maximumEntries;
    }

    public string BackendKind { get; }

    public int MaximumEntries { get; }

    public static IPacketCodec<GatewayBackendServerListRequest> Codec { get; } = new GatewayBackendServerListRequestCodec();

    private sealed class GatewayBackendServerListRequestCodec : IPacketCodec<GatewayBackendServerListRequest>
    {
        public int GetPayloadSize(GatewayBackendServerListRequest value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            return PacketWriter.GetStringSize(value.BackendKind) + sizeof(int);
        }

        public void Encode(GatewayBackendServerListRequest value, ref PacketWriter writer)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            writer.WriteString(value.BackendKind);
            writer.WriteInt32(value.MaximumEntries);
        }

        public GatewayBackendServerListRequest Decode(ref PacketReader reader)
        {
            return new GatewayBackendServerListRequest(
                reader.ReadString(),
                reader.ReadInt32());
        }
    }
}
