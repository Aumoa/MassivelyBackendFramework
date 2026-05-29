using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PacketCore;

public static class PacketFrameWriter
{
    public static ValueTask WriteAsync(
        Stream stream,
        PacketFrame frame,
        CancellationToken cancellationToken = default)
    {
        if (frame == null)
        {
            throw new ArgumentNullException(nameof(frame));
        }

        return WriteAsync(stream, frame.Header, frame.Payload, cancellationToken);
    }

    public static async ValueTask WriteAsync(
        Stream stream,
        PacketHeader header,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default)
    {
        if (stream == null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        if (payload.Length != header.PayloadLength)
        {
            throw new PacketFormatException(PacketValidationError.PayloadLengthMismatch);
        }

        var headerBuffer = ArrayPool<byte>.Shared.Rent(PacketHeader.Size);
        try
        {
            header.Write(headerBuffer.AsSpan(0, PacketHeader.Size));
            await stream.WriteAsync(headerBuffer.AsMemory(0, PacketHeader.Size), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(headerBuffer);
        }

        if (payload.Length > 0)
        {
            await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        }
    }
}
