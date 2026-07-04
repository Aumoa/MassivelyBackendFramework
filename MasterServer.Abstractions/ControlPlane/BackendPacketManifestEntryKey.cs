using System;
using PacketCore;

namespace MasterServer.ControlPlane;

internal readonly struct BackendPacketManifestEntryKey : IEquatable<BackendPacketManifestEntryKey>
{
    public BackendPacketManifestEntryKey(
        BackendPacketManifestDirection direction,
        PacketKind packetKind,
        ushort packetId,
        ushort routedVersion)
    {
        Direction = direction;
        PacketKind = packetKind;
        PacketId = packetId;
        RoutedVersion = routedVersion;
    }

    public BackendPacketManifestDirection Direction { get; }

    public PacketKind PacketKind { get; }

    public ushort PacketId { get; }

    public ushort RoutedVersion { get; }

    public bool Equals(BackendPacketManifestEntryKey other)
    {
        return Direction == other.Direction &&
               PacketKind == other.PacketKind &&
               PacketId == other.PacketId &&
               RoutedVersion == other.RoutedVersion;
    }

    public override bool Equals(object? obj)
    {
        return obj is BackendPacketManifestEntryKey other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Direction, PacketKind, PacketId, RoutedVersion);
    }
}
