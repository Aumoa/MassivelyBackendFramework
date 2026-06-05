using System;
using PacketCore;

namespace RemoteDebugServer.Protocols;

public static class RemoteDebugProtocol
{
    public const ushort SchemaVersion = 1;
    public const int AuthNonceLength = 32;
    public const int MaxHandshakePayloadLength = 16 * 1024;

    public static readonly PacketReadPolicy UntrustedHandshakePolicy = new PacketReadPolicy(
        PacketKindMask.Control,
        MaxHandshakePayloadLength,
        rejectUnknownFlags: true);

    public static void ValidateHandshakeFrame(PacketFrame frame, ushort expectedPacketId)
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
                $"Unexpected RemoteDebug handshake packet. Expected id {expectedPacketId}, received kind {header.Kind}, id {header.PacketId}, version {header.Version}.");
        }
    }
}
