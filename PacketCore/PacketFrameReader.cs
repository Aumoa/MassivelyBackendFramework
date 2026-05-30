using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PacketCore;

public static class PacketFrameReader
{
    public static async ValueTask<PacketFrame?> ReadAsync(
        Stream stream,
        PacketReadPolicy policy,
        CancellationToken cancellationToken = default)
    {
        if (stream == null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        var headerBuffer = ArrayPool<byte>.Shared.Rent(PacketHeader.Size);
        PacketHeader header;

        try
        {
            bool hasHeader = await TryReadExactlyAsync(
                stream,
                headerBuffer.AsMemory(0, PacketHeader.Size),
                allowEndOfStreamBeforeFirstByte: true,
                cancellationToken).ConfigureAwait(false);

            if (!hasHeader)
            {
                return null;
            }

            if (!PacketHeader.TryRead(headerBuffer.AsSpan(0, PacketHeader.Size), policy, out header, out var error))
            {
                throw new PacketFormatException(error);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(headerBuffer);
        }

        OwnedPacketBuffer? payload = null;
        try
        {
            if (header.PayloadLength > 0)
            {
                payload = OwnedPacketBuffer.Rent(header.PayloadLength);
                await TryReadExactlyAsync(
                    stream,
                    payload.Memory,
                    allowEndOfStreamBeforeFirstByte: false,
                    cancellationToken).ConfigureAwait(false);
            }

            var frame = new PacketFrame(header, payload);
            payload = null;
            return frame;
        }
        finally
        {
            payload?.Dispose();
        }
    }

    private static async ValueTask<bool> TryReadExactlyAsync(
        Stream stream,
        Memory<byte> buffer,
        bool allowEndOfStreamBeforeFirstByte,
        CancellationToken cancellationToken)
    {
        int readTotal = 0;
        while (readTotal < buffer.Length)
        {
            int bytesRead = await stream.ReadAsync(buffer.Slice(readTotal), cancellationToken).ConfigureAwait(false);
            if (bytesRead == 0)
            {
                if (readTotal == 0 && allowEndOfStreamBeforeFirstByte)
                {
                    return false;
                }

                throw new EndOfStreamException($"Expected {buffer.Length} bytes but only received {readTotal} bytes.");
            }

            readTotal += bytesRead;
        }

        return true;
    }
}
