using System;
using System.Buffers;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GatewayServer.Utility;
using PacketCore;

namespace GatewayServer.Protocols;

public struct GatewayHandshakeNotify : IProtocolStructure
{
    public string LoginUri { get; set; }

    public readonly int CalculateSize()
    {
        return BufferUtility.CalculateSizeForString(LoginUri);
    }

    public readonly async ValueTask WriteToAsync(Stream stream, int calculatedSize, CancellationToken cancellationToken = default)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(calculatedSize);
        try
        {
            await BufferUtility.WriteAsync(stream, buffer, LoginUri, cancellationToken);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static async ValueTask<GatewayHandshakeNotify> ReadFromAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(4);
        int bytesRead;
        try
        {
            await stream.ReadExactly2Async(buffer, cancellationToken);
            bytesRead = BitConverter.ToInt32(buffer, 0);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        var stringBuffer = ArrayPool<byte>.Shared.Rent(bytesRead);
        try
        {
            await stream.ReadExactly2Async(buffer, cancellationToken);
            string s = Encoding.UTF8.GetString(stringBuffer);
            return new GatewayHandshakeNotify
            {
                LoginUri = s
            };
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(stringBuffer);
        }
    }
}
