using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using PacketCore.Utility;

namespace PacketCore;

public class Packet
{
    public const int PROTOCOL_TYPE_BITS = 3;
    public const int PROTOCOL_TYPE_REQ = 0x01;
    public const int PROTOCOL_TYPE_ACK = 0x02;
    public const int PROTOCOL_TYPE_NFY = 0x03;
    public const int PROTOCOL_RESERVED_BITS = 5;
    public const int PROTOCOL_ID_BITS = 16;
    public const int PROTOCOL_VERSION_BITS = 16;
    public const int PROTOCOL_PAYLOAD_SIZE_BITS = 24;
    public const int PROTOCOL_HEADER_SIZE_IN_BYTES = (PROTOCOL_TYPE_BITS + PROTOCOL_RESERVED_BITS + PROTOCOL_ID_BITS + PROTOCOL_VERSION_BITS + PROTOCOL_PAYLOAD_SIZE_BITS) / 8;

    private RentedArray<byte> m_Header;
    private RentedArray<byte> m_Payload;

    public ReadOnlySpan<byte> Payload => m_Payload ? m_Payload.AsReadOnlySpan() : ReadOnlySpan<byte>.Empty;

    public void ReadyForReuse()
    {
        if (m_Header)
        {
            m_Header.Dispose();
            m_Header = default;
        }

        if (m_Payload)
        {
            m_Payload.Dispose();
            m_Payload = default;
        }
    }

    public async ValueTask WriteToAsync(Stream s, CancellationToken cancellationToken = default)
    {
        await s.WriteAsync(m_Header.AsReadOnlyMemory(), cancellationToken);
        if (m_Payload)
        {
            await s.WriteAsync(m_Payload.AsReadOnlyMemory(), cancellationToken);
        }
    }

    public void Initialize(RentedArray<byte> header, RentedArray<byte> payload)
    {
        Debug.Assert(!m_Header);
        m_Header = header;
        m_Payload = payload;
    }

    public void Initialize(int protocolType, int protocolId, int protocolVersion, RentedArray<byte> payload)
    {
        Debug.Assert(!m_Header);

        if (protocolType is not (PROTOCOL_TYPE_REQ or PROTOCOL_TYPE_ACK or PROTOCOL_TYPE_NFY))
        {
            throw new ArgumentException("Invalid protocol type", nameof(protocolType));
        }

        if (protocolId == 0)
        {
            throw new ArgumentException("Protocol ID cannot be 0", nameof(protocolId));
        }

        if (protocolVersion == 0)
        {
            throw new ArgumentException("Protocol version cannot be 0", nameof(protocolVersion));
        }

        m_Header =  RentedArray<byte>.Get(PROTOCOL_HEADER_SIZE_IN_BYTES);
        m_Payload = payload;

        var headerSpan = m_Header.AsSpan(0, PROTOCOL_HEADER_SIZE_IN_BYTES);

        // Byte 0: protocolType (placed in upper bits, shifted left by 6 bits)
        headerSpan[0] = (byte)(protocolType << 6);

        // Byte 1-2: protocolId (network byte order - big-endian)
        BinaryPrimitives.WriteUInt16BigEndian(headerSpan.Slice(1, 2), (ushort)protocolId);

        // Byte 3-4: protocolVersion (network byte order - big-endian)
        BinaryPrimitives.WriteUInt16BigEndian(headerSpan.Slice(3, 2), (ushort)protocolVersion);

        // Byte 5-6-7: payloadSize (network byte order - big-endian, 24 bits)
        int payloadSize = payload.Length;
        headerSpan[5] = (byte)(payloadSize >> 16);
        headerSpan[6] = (byte)(payloadSize >> 8);
        headerSpan[7] = (byte)payloadSize;
    }

    public static void EnsureHeaderValidation(in RentedArray<byte> headerSpan, out int protocolType, out int protocolId, out int protocolVersion, out int payloadSize)
    {
        if (headerSpan.Length < PROTOCOL_HEADER_SIZE_IN_BYTES)
        {
            throw new InvalidOperationException("Invalid header size");
        }

        protocolType = headerSpan[0] >> 6;
        if (protocolType == 0)
        {
            throw new InvalidOperationException("Invalid protocol type");
        }

        protocolId = BinaryPrimitives.ReadUInt16BigEndian(headerSpan.AsSpan(1, 2));
        if (protocolId == 0)
        {
            throw new InvalidOperationException("Protocol ID cannot be 0");
        }

        protocolVersion = BinaryPrimitives.ReadUInt16BigEndian(headerSpan.AsSpan(3, 2));
        if (protocolVersion == 0)
        {
            throw new InvalidOperationException("Protocol version cannot be 0");
        }

        payloadSize = (headerSpan[5] << 16) | (headerSpan[6] << 8) | headerSpan[7];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void EnsureClientHeaderValidation(in RentedArray<byte> headerSpan, out int protocolType, out int protocolId, out int protocolVersion, out int payloadSize)
    {
        EnsureHeaderValidation(headerSpan, out protocolType, out protocolId, out protocolVersion, out payloadSize);
        if (protocolType != PROTOCOL_TYPE_REQ)
        {
            // Only request packets are accepted from clients. If the protocol type is not a request, it is considered a protocol violation and the connection is terminated.
            throw new InvalidOperationException("Protocol violation: Only request packets are accepted from clients.");
        }
    }
}
