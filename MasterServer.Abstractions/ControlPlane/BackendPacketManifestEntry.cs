using System;
using PacketCore;

namespace MasterServer.ControlPlane;

public sealed class BackendPacketManifestEntry
{
    public BackendPacketManifestEntry(
        BackendPacketManifestDirection direction,
        PacketKind packetKind,
        ushort packetId,
        ushort routedVersion,
        BackendPacketPayloadConstraint payloadConstraint,
        BackendPacketManifestEntryStatus status = BackendPacketManifestEntryStatus.Active)
    {
        if (!IsValidDirection(direction))
        {
            throw new ArgumentOutOfRangeException(nameof(direction));
        }

        if (packetKind is not (PacketKind.Request or PacketKind.Response or PacketKind.Notify))
        {
            throw new ArgumentOutOfRangeException(nameof(packetKind));
        }

        if (packetId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(packetId));
        }

        if (routedVersion == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(routedVersion));
        }

        if (!Enum.IsDefined(typeof(BackendPacketManifestEntryStatus), status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        Direction = direction;
        PacketKind = packetKind;
        PacketId = packetId;
        RoutedVersion = routedVersion;
        PayloadConstraint = payloadConstraint ?? throw new ArgumentNullException(nameof(payloadConstraint));
        Status = status;
    }

    public BackendPacketManifestDirection Direction { get; }

    public PacketKind PacketKind { get; }

    public ushort PacketId { get; }

    public ushort RoutedVersion { get; }

    public BackendPacketPayloadConstraint PayloadConstraint { get; }

    public BackendPacketManifestEntryStatus Status { get; }

    internal BackendPacketManifestEntryKey Key => new(Direction, PacketKind, PacketId, RoutedVersion);

    internal static bool IsValidDirection(BackendPacketManifestDirection direction)
    {
        return direction is BackendPacketManifestDirection.ClientToBackend or BackendPacketManifestDirection.BackendToClient;
    }
}
