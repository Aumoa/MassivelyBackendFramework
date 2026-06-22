using System;
using PacketCore;

namespace MasterServer.ControlPlane;

public sealed class GatewayBackendRoutePolicySnapshot
{
    private const int MaxBackendKindCount = 2048;

    public GatewayBackendRoutePolicySnapshot(
        string[] allowedBackendKinds,
        DateTimeOffset observedAt)
    {
        AllowedBackendKinds = allowedBackendKinds ?? throw new ArgumentNullException(nameof(allowedBackendKinds));
        ObservedAt = observedAt;
    }

    public string[] AllowedBackendKinds { get; }

    public DateTimeOffset ObservedAt { get; }

    public static IPacketCodec<GatewayBackendRoutePolicySnapshot> Codec { get; } = new GatewayBackendRoutePolicySnapshotCodec();

    private sealed class GatewayBackendRoutePolicySnapshotCodec : IPacketCodec<GatewayBackendRoutePolicySnapshot>
    {
        public int GetPayloadSize(GatewayBackendRoutePolicySnapshot value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            int size = sizeof(int) + sizeof(long);
            foreach (var backendKind in value.AllowedBackendKinds)
            {
                size += PacketWriter.GetStringSize(backendKind);
            }

            return size;
        }

        public void Encode(GatewayBackendRoutePolicySnapshot value, ref PacketWriter writer)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            writer.WriteInt32(value.AllowedBackendKinds.Length);
            foreach (var backendKind in value.AllowedBackendKinds)
            {
                writer.WriteString(backendKind);
            }

            writer.WriteInt64(value.ObservedAt.ToUnixTimeMilliseconds());
        }

        public GatewayBackendRoutePolicySnapshot Decode(ref PacketReader reader)
        {
            int backendKindCount = reader.ReadInt32();
            if (backendKindCount < 0 || backendKindCount > MaxBackendKindCount)
            {
                throw new PacketFormatException(PacketValidationError.InvalidStringLength, "Invalid Gateway Backend route policy kind count.");
            }

            var allowedBackendKinds = new string[backendKindCount];
            for (int i = 0; i < allowedBackendKinds.Length; i++)
            {
                allowedBackendKinds[i] = reader.ReadString();
            }

            var observedAt = DateTimeOffset.FromUnixTimeMilliseconds(reader.ReadInt64());
            return new GatewayBackendRoutePolicySnapshot(allowedBackendKinds, observedAt);
        }
    }
}
