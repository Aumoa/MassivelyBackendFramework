using System;
using System.Buffers.Binary;
using System.Text;

namespace PacketCore;

public ref struct PacketWriter
{
    private readonly Span<byte> m_Buffer;
    private int m_Position;

    public PacketWriter(Span<byte> buffer)
    {
        m_Buffer = buffer;
        m_Position = 0;
    }

    public int WrittenCount => m_Position;

    public int Remaining => m_Buffer.Length - m_Position;

    public static int GetStringSize(string value)
    {
        if (value == null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        return 4 + Encoding.UTF8.GetByteCount(value);
    }

    public void WriteByte(byte value)
    {
        EnsureAvailable(1);
        m_Buffer[m_Position++] = value;
    }

    public void WriteUInt16(ushort value)
    {
        EnsureAvailable(2);
        BinaryPrimitives.WriteUInt16BigEndian(m_Buffer.Slice(m_Position, 2), value);
        m_Position += 2;
    }

    public void WriteUInt32(uint value)
    {
        EnsureAvailable(4);
        BinaryPrimitives.WriteUInt32BigEndian(m_Buffer.Slice(m_Position, 4), value);
        m_Position += 4;
    }

    public void WriteInt32(int value)
    {
        EnsureAvailable(4);
        BinaryPrimitives.WriteInt32BigEndian(m_Buffer.Slice(m_Position, 4), value);
        m_Position += 4;
    }

    public void WriteString(string value)
    {
        if (value == null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        int byteCount = Encoding.UTF8.GetByteCount(value);
        WriteInt32(byteCount);
        EnsureAvailable(byteCount);
        Encoding.UTF8.GetBytes(value.AsSpan(), m_Buffer.Slice(m_Position, byteCount));
        m_Position += byteCount;
    }

    public void WriteBytes(ReadOnlySpan<byte> value)
    {
        EnsureAvailable(value.Length);
        value.CopyTo(m_Buffer.Slice(m_Position, value.Length));
        m_Position += value.Length;
    }

    public bool TryWriteUInt16(ushort value)
    {
        if (Remaining < 2)
        {
            return false;
        }

        WriteUInt16(value);
        return true;
    }

    public bool TryWriteString(string value)
    {
        if (value == null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        int byteCount = Encoding.UTF8.GetByteCount(value);
        if (Remaining < 4 + byteCount)
        {
            return false;
        }

        WriteString(value);
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
