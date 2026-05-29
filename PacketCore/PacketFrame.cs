using System;

namespace PacketCore;

public sealed class PacketFrame : IDisposable
{
    private OwnedPacketBuffer? m_PayloadOwner;
    private readonly ReadOnlyMemory<byte> m_BorrowedPayload;

    public PacketFrame(PacketHeader header, OwnedPacketBuffer? payloadOwner)
    {
        if (header.PayloadLength == 0)
        {
            if (payloadOwner != null && payloadOwner.Length != 0)
            {
                throw new ArgumentException("Payload owner length must match the header payload length.", nameof(payloadOwner));
            }
        }
        else if (payloadOwner == null || payloadOwner.Length != header.PayloadLength)
        {
            throw new ArgumentException("Payload owner length must match the header payload length.", nameof(payloadOwner));
        }

        Header = header;
        m_PayloadOwner = payloadOwner;
        m_BorrowedPayload = ReadOnlyMemory<byte>.Empty;
    }

    private PacketFrame(PacketHeader header, ReadOnlyMemory<byte> borrowedPayload)
    {
        if (borrowedPayload.Length != header.PayloadLength)
        {
            throw new ArgumentException("Payload length must match the header payload length.", nameof(borrowedPayload));
        }

        Header = header;
        m_BorrowedPayload = borrowedPayload;
    }

    public PacketHeader Header { get; }

    public ReadOnlyMemory<byte> Payload
    {
        get
        {
            var owner = m_PayloadOwner;
            return owner != null ? owner.Memory : m_BorrowedPayload;
        }
    }

    public static PacketFrame Create(
        PacketKind kind,
        ushort packetId,
        ushort version,
        ReadOnlyMemory<byte> payload,
        PacketFlags flags = PacketFlags.None)
    {
        var owner = OwnedPacketBuffer.Rent(payload.Length);
        try
        {
            payload.CopyTo(owner.Memory);
            var header = new PacketHeader(kind, flags, packetId, version, payload.Length);
            var frame = new PacketFrame(header, owner);
            owner = null!;
            return frame;
        }
        finally
        {
            owner?.Dispose();
        }
    }

    public static PacketFrame Borrow(
        PacketKind kind,
        ushort packetId,
        ushort version,
        ReadOnlyMemory<byte> payload,
        PacketFlags flags = PacketFlags.None)
    {
        var header = new PacketHeader(kind, flags, packetId, version, payload.Length);
        return new PacketFrame(header, payload);
    }

    public void Dispose()
    {
        m_PayloadOwner?.Dispose();
        m_PayloadOwner = null;
    }
}
