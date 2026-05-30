using System;

namespace PacketCore;

public readonly struct PacketReadPolicy
{
    public const int DefaultUntrustedMaxPayloadLength = 1024 * 1024;
    public const int DefaultTrustedMaxPayloadLength = PacketHeader.MaxPayloadLength;

    public static readonly PacketReadPolicy UntrustedClient = new PacketReadPolicy(
        PacketKindMask.Request | PacketKindMask.Notify,
        DefaultUntrustedMaxPayloadLength,
        rejectUnknownFlags: true);

    public static readonly PacketReadPolicy TrustedServer = new PacketReadPolicy(
        PacketKindMask.All,
        DefaultTrustedMaxPayloadLength,
        rejectUnknownFlags: false);

    public PacketReadPolicy(
        PacketKindMask allowedKinds,
        int maxPayloadLength,
        bool rejectUnknownFlags)
    {
        if (allowedKinds == PacketKindMask.None)
        {
            throw new ArgumentException("At least one packet kind must be allowed.", nameof(allowedKinds));
        }

        if (maxPayloadLength < 0 || maxPayloadLength > PacketHeader.MaxPayloadLength)
        {
            throw new ArgumentOutOfRangeException(nameof(maxPayloadLength));
        }

        AllowedKinds = allowedKinds;
        MaxPayloadLength = maxPayloadLength;
        RejectUnknownFlags = rejectUnknownFlags;
    }

    public PacketKindMask AllowedKinds { get; }

    public int MaxPayloadLength { get; }

    public bool RejectUnknownFlags { get; }

    public bool AllowsKind(PacketKind kind)
    {
        return (AllowedKinds & ToMask(kind)) != 0;
    }

    private static PacketKindMask ToMask(PacketKind kind)
    {
        return (PacketKindMask)(1 << (int)kind);
    }
}
