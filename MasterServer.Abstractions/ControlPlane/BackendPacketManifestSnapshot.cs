using System;
using PacketCore;

namespace MasterServer.ControlPlane;

public sealed class BackendPacketManifestSnapshot
{
    public BackendPacketManifestSnapshot(
        BackendPacketManifest[] manifests,
        DateTimeOffset observedAt)
    {
        Manifests = manifests ?? throw new ArgumentNullException(nameof(manifests));
        ObservedAt = observedAt;
    }

    public BackendPacketManifest[] Manifests { get; }

    public DateTimeOffset ObservedAt { get; }

    public static IPacketCodec<BackendPacketManifestSnapshot> Codec { get; } = new BackendPacketManifestSnapshotCodec();

    private sealed class BackendPacketManifestSnapshotCodec : IPacketCodec<BackendPacketManifestSnapshot>
    {
        public int GetPayloadSize(BackendPacketManifestSnapshot value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            int size = sizeof(int) + sizeof(long);
            foreach (var manifest in value.Manifests)
            {
                size += BackendPacketManifestPacketCodec.GetManifestSize(manifest);
            }

            return size;
        }

        public void Encode(BackendPacketManifestSnapshot value, ref PacketWriter writer)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            writer.WriteInt32(value.Manifests.Length);
            foreach (var manifest in value.Manifests)
            {
                BackendPacketManifestPacketCodec.WriteManifest(manifest, ref writer);
            }

            writer.WriteInt64(value.ObservedAt.ToUnixTimeMilliseconds());
        }

        public BackendPacketManifestSnapshot Decode(ref PacketReader reader)
        {
            int manifestCount = reader.ReadInt32();
            if (manifestCount < 0 || manifestCount > BackendPacketManifestPacketCodec.MaxManifestCount)
            {
                throw new PacketFormatException(PacketValidationError.InvalidStringLength, "Invalid Backend packet manifest count.");
            }

            var manifests = new BackendPacketManifest[manifestCount];
            for (int i = 0; i < manifests.Length; i++)
            {
                manifests[i] = BackendPacketManifestPacketCodec.ReadManifest(ref reader);
            }

            return new BackendPacketManifestSnapshot(
                manifests,
                DateTimeOffset.FromUnixTimeMilliseconds(reader.ReadInt64()));
        }
    }
}
