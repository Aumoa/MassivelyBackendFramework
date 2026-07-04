using System;
using PacketCore;

namespace MasterServer.ControlPlane;

public static class MasterControlProtocol
{
    public const ushort SchemaVersion = 8;
    public const int AuthNonceLength = 32;
    public const int MaxHandshakePayloadLength = 16 * 1024;

    public static readonly PacketReadPolicy UntrustedHandshakePolicy = new PacketReadPolicy(
        PacketKindMask.Control,
        MaxHandshakePayloadLength,
        rejectUnknownFlags: true);

    public static readonly PacketReadPolicy TrustedControlPlanePolicy = PacketReadPolicy.TrustedServer;

    public static void ValidateControlFrame(PacketFrame frame, ushort expectedPacketId)
    {
        if (frame == null)
        {
            throw new ArgumentNullException(nameof(frame));
        }

        var header = frame.Header;
        if (header.Kind != PacketKind.Control ||
            header.Flags != PacketFlags.None ||
            header.PacketId != expectedPacketId ||
            header.Version != SchemaVersion)
        {
            throw new InvalidOperationException(
                $"Unexpected Master control packet. Expected id {expectedPacketId}, received kind {header.Kind}, id {header.PacketId}, version {header.Version}.");
        }
    }
}
