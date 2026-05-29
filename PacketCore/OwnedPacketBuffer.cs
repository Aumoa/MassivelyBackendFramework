using System;
using System.Buffers;

namespace PacketCore;

public sealed class OwnedPacketBuffer : IMemoryOwner<byte>
{
    private byte[]? m_Buffer;
    private readonly int m_Length;
    private readonly bool m_ClearOnReturn;

    private OwnedPacketBuffer(byte[] buffer, int length, bool clearOnReturn)
    {
        m_Buffer = buffer;
        m_Length = length;
        m_ClearOnReturn = clearOnReturn;
    }

    public Memory<byte> Memory
    {
        get
        {
            var buffer = m_Buffer;
            if (buffer == null)
            {
                throw new ObjectDisposedException(nameof(OwnedPacketBuffer));
            }

            return buffer.AsMemory(0, m_Length);
        }
    }

    public Span<byte> Span => Memory.Span;

    public int Length => m_Length;

    public static OwnedPacketBuffer Rent(int length, bool clearOnReturn = false)
    {
        if (length < 0 || length > PacketHeader.MaxPayloadLength)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        var buffer = ArrayPool<byte>.Shared.Rent(length);
        return new OwnedPacketBuffer(buffer, length, clearOnReturn);
    }

    public void Dispose()
    {
        var buffer = m_Buffer;
        if (buffer == null)
        {
            return;
        }

        m_Buffer = null;
        ArrayPool<byte>.Shared.Return(buffer, m_ClearOnReturn);
    }
}
