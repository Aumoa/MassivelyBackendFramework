using System;
using System.Buffers.Binary;

namespace PacketCore;

public readonly struct PacketHeader
{
    public const int Size = 8;
    public const int MaxPayloadLength = 0xFFFFFF;

    private const byte KindShift = 6;
    private const byte FlagsMask = 0x3F;

    public PacketHeader(PacketKind kind, PacketFlags flags, ushort packetId, ushort version, int payloadLength)
    {
        if (!IsValidKind(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if (((byte)flags & ~FlagsMask) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(flags));
        }

        if (packetId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(packetId));
        }

        if (version == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(version));
        }

        if (payloadLength < 0 || payloadLength > MaxPayloadLength)
        {
            throw new ArgumentOutOfRangeException(nameof(payloadLength));
        }

        Kind = kind;
        Flags = flags;
        PacketId = packetId;
        Version = version;
        PayloadLength = payloadLength;
    }

    public PacketKind Kind { get; }

    public PacketFlags Flags { get; }

    public ushort PacketId { get; }

    public ushort Version { get; }

    public int PayloadLength { get; }

    public void Write(Span<byte> destination)
    {
        if (!TryWrite(destination))
        {
            throw new ArgumentException("Destination is too small for a packet header.", nameof(destination));
        }
    }

    public bool TryWrite(Span<byte> destination)
    {
        if (destination.Length < Size)
        {
            return false;
        }

        destination[0] = (byte)(((byte)Kind << KindShift) | ((byte)Flags & FlagsMask));
        BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(1, 2), PacketId);
        BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(3, 2), Version);
        destination[5] = (byte)(PayloadLength >> 16);
        destination[6] = (byte)(PayloadLength >> 8);
        destination[7] = (byte)PayloadLength;
        return true;
    }

    public static bool TryRead(
        ReadOnlySpan<byte> source,
        PacketReadPolicy policy,
        out PacketHeader header,
        out PacketValidationError error)
    {
        header = default;

        if (source.Length < Size)
        {
            error = PacketValidationError.HeaderTooSmall;
            return false;
        }

        var kind = (PacketKind)(source[0] >> KindShift);
        if (!IsValidKind(kind) || !policy.AllowsKind(kind))
        {
            error = PacketValidationError.PacketKindNotAllowed;
            return false;
        }

        var flags = (PacketFlags)(source[0] & FlagsMask);
        if (policy.RejectUnknownFlags && flags != PacketFlags.None)
        {
            error = PacketValidationError.UnknownFlags;
            return false;
        }

        ushort packetId = BinaryPrimitives.ReadUInt16BigEndian(source.Slice(1, 2));
        if (packetId == 0)
        {
            error = PacketValidationError.PacketIdZero;
            return false;
        }

        ushort version = BinaryPrimitives.ReadUInt16BigEndian(source.Slice(3, 2));
        if (version == 0)
        {
            error = PacketValidationError.VersionZero;
            return false;
        }

        int payloadLength = (source[5] << 16) | (source[6] << 8) | source[7];
        if (payloadLength > policy.MaxPayloadLength)
        {
            error = PacketValidationError.PayloadLengthTooLarge;
            return false;
        }

        header = new PacketHeader(kind, flags, packetId, version, payloadLength);
        error = PacketValidationError.None;
        return true;
    }

    private static bool IsValidKind(PacketKind kind)
    {
        return kind is PacketKind.Request or PacketKind.Response or PacketKind.Notify or PacketKind.Control;
    }
}
