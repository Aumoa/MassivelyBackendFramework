using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace OAuth2.Services;

internal static class AccountPictureRemoteConnection
{
    public static SocketsHttpHandler CreateHandler()
    {
        return new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            ConnectCallback = ConnectAsync
        };
    }

    public static async ValueTask<bool> IsPublicRemoteHostAsync(Uri uri, CancellationToken cancellationToken)
    {
        IPAddress[] addresses;
        try
        {
            addresses = await ResolveAddressesAsync(uri.DnsSafeHost, cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        return AreAllPublicAddresses(addresses);
    }

    internal static async ValueTask<Stream> ConnectToPublicAddressAsync(
        DnsEndPoint endpoint,
        Func<string, CancellationToken, ValueTask<IPAddress[]>> resolveAddressesAsync,
        Func<IPAddress, int, CancellationToken, ValueTask<Stream>> connectAsync,
        CancellationToken cancellationToken)
    {
        var addresses = await resolveAddressesAsync(endpoint.Host, cancellationToken);
        if (!AreAllPublicAddresses(addresses))
        {
            throw new HttpRequestException("The remote account picture host did not resolve to a public address.");
        }

        Exception? lastException = null;
        foreach (var address in addresses)
        {
            try
            {
                return await connectAsync(address, endpoint.Port, cancellationToken);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested &&
                                             (exception is SocketException or IOException))
            {
                lastException = exception;
            }
        }

        throw new HttpRequestException("The remote account picture host could not be reached.", lastException);
    }

    internal static bool IsPublicAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address) ||
            IPAddress.Any.Equals(address) ||
            IPAddress.IPv6Any.Equals(address) ||
            IPAddress.None.Equals(address) ||
            IPAddress.IPv6None.Equals(address))
        {
            return false;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] switch
            {
                0 or 10 or 127 => false,
                100 when bytes[1] is >= 64 and <= 127 => false,
                169 when bytes[1] == 254 => false,
                172 when bytes[1] is >= 16 and <= 31 => false,
                192 when bytes[1] == 168 => false,
                198 when bytes[1] is 18 or 19 => false,
                >= 224 => false,
                _ => true
            };
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var bytes = address.GetAddressBytes();
            return !address.IsIPv6LinkLocal &&
                   !address.IsIPv6Multicast &&
                   !address.IsIPv6SiteLocal &&
                   !address.IsIPv6Teredo &&
                   bytes[0] is not 0xfc and not 0xfd;
        }

        return false;
    }

    private static async ValueTask<Stream> ConnectAsync(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        return await ConnectToPublicAddressAsync(
            context.DnsEndPoint,
            ResolveAddressesAsync,
            OpenSocketStreamAsync,
            cancellationToken);
    }

    private static async ValueTask<IPAddress[]> ResolveAddressesAsync(string host, CancellationToken cancellationToken)
    {
        return await Dns.GetHostAddressesAsync(host, cancellationToken);
    }

    private static async ValueTask<Stream> OpenSocketStreamAsync(
        IPAddress address,
        int port,
        CancellationToken cancellationToken)
    {
        var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            await socket.ConnectAsync(new IPEndPoint(address, port), cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static bool AreAllPublicAddresses(IPAddress[] addresses)
    {
        return addresses.Length > 0 && addresses.All(IsPublicAddress);
    }
}
