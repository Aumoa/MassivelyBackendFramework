using System;
using System.Buffers.Binary;
using System.Text;

namespace PacketCore;

public ref struct PacketReader
{
    private readonly ReadOnlySpan<byte> m_Buffer;
    private int m_Position;

    public PacketReader(ReadOnlySpan<byte> buffer)
    {
        m_Buffer = buffer;
        m_Position = 0;
    }

    public int Consumed => m_Position;

    public int Remaining => m_Buffer.Length - m_Position;

    public byte ReadByte()
    {
        EnsureAvailable(1);
        return m_Buffer[m_Position++];
    }

    public ushort ReadUInt16()
    {
        EnsureAvailable(2);
        ushort value = BinaryPrimitives.ReadUInt16BigEndian(m_Buffer.Slice(m_Position, 2));
        m_Position += 2;
        return value;
    }

    public uint ReadUInt32()
    {
        EnsureAvailable(4);
        uint value = BinaryPrimitives.ReadUInt32BigEndian(m_Buffer.Slice(m_Position, 4));
        m_Position += 4;
        return value;
    }

    public int ReadInt32()
    {
        EnsureAvailable(4);
        int value = BinaryPrimitives.ReadInt32BigEndian(m_Buffer.Slice(m_Position, 4));
        m_Position += 4;
        return value;
    }

    public long ReadInt64()
    {
        EnsureAvailable(8);
        long value = BinaryPrimitives.ReadInt64BigEndian(m_Buffer.Slice(m_Position, 8));
        m_Position += 8;
        return value;
    }

    public Guid ReadGuid()
    {
        EnsureAvailable(16);
        var value = new Guid(m_Buffer.Slice(m_Position, 16));
        m_Position += 16;
        return value;
    }

    public string ReadString()
    {
        int byteCount = ReadInt32();
        if (byteCount < 0)
        {
            throw new PacketFormatException(PacketValidationError.InvalidStringLength);
        }

        EnsureAvailable(byteCount);
        string value = Encoding.UTF8.GetString(m_Buffer.Slice(m_Position, byteCount));
        m_Position += byteCount;
        return value;
    }

    public ReadOnlySpan<byte> ReadBytes(int length)
    {
        if (length < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        EnsureAvailable(length);
        var value = m_Buffer.Slice(m_Position, length);
        m_Position += length;
        return value;
    }

    public bool TryReadUInt16(out ushort value)
    {
        if (Remaining < 2)
        {
            value = 0;
            return false;
        }

        value = ReadUInt16();
        return true;
    }

    public bool TryReadString(out string value)
    {
        value = string.Empty;
        if (Remaining < 4)
        {
            return false;
        }

        int position = m_Position;
        int byteCount = ReadInt32();
        if (byteCount < 0 || Remaining < byteCount)
        {
            m_Position = position;
            return false;
        }

        value = Encoding.UTF8.GetString(m_Buffer.Slice(m_Position, byteCount));
        m_Position += byteCount;
        return true;
    }

    private void EnsureAvailable(int count)
    {
        if (Remaining < count)
        {
            throw new PacketFormatException(PacketValidationError.PayloadTooSmall);
        }
    }
}
