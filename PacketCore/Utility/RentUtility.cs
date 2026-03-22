using System.Buffers;
using System.Text;

namespace PacketCore.Utility;

public static class RentUtility
{
    public static RentedArray<byte> RentFor(string message, Encoding encoding)
    {
        var sizeInBytes = encoding.GetByteCount(message);
        var rentArray = ArrayPool<byte>.Shared.Rent(sizeInBytes);
        encoding.GetBytes(message, rentArray);
        return new RentedArray<byte>(rentArray, sizeInBytes);
    }

    public static RentedArray<byte> RentFor(string message) => RentFor(message, Encoding.UTF8);
}
