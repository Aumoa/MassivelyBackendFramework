using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GatewayServer.Utility;

public static class BufferUtility
{
    public static int CalculateSizeForString(string s)
    {
        return 4 + Encoding.UTF8.GetMaxByteCount(s.Length);
    }

    public static async ValueTask<int> WriteAsync(Stream s, Memory<byte> buffer, string value, CancellationToken cancellationToken = default)
    {
        var bytesWritten = Encoding.UTF8.GetBytes(value, buffer[4..].Span);
        BitConverter.TryWriteBytes(buffer.Span, bytesWritten);
        await s.WriteAsync(buffer[.. (4 + bytesWritten)], cancellationToken);
        return 4 + bytesWritten;
    }
}
