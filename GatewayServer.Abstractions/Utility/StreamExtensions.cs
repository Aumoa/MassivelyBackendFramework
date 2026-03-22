using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GatewayServer.Utility;

internal static class StreamExtensions
{
    public static async Task ReadExactly2Async(this Stream s, Memory<byte> b, CancellationToken cancellationToken = default)
    {
        int written = 0;
        while (written < b.Length)
        {
            int read = await s.ReadAsync(b.Slice(written), cancellationToken);
            if (read == 0)
            {
                throw new EndOfStreamException($"Expected to read {b.Length} bytes, but only read {written} bytes before reaching the end of the stream.");
            }
            written += read;
        }
    }
}
