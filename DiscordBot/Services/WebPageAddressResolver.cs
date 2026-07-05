using System.Net;
using System.Net.Sockets;

namespace DiscordBot.Services;

internal interface IWebPageAddressResolver
{
    ValueTask<IReadOnlyList<IPAddress>> GetHostAddressesAsync(
        string host,
        CancellationToken cancellationToken = default);
}

internal sealed class DnsWebPageAddressResolver : IWebPageAddressResolver
{
    public async ValueTask<IReadOnlyList<IPAddress>> GetHostAddressesAsync(
        string host,
        CancellationToken cancellationToken = default)
    {
        return await Dns.GetHostAddressesAsync(host, cancellationToken);
    }
}

internal static class WebPageAddressSafety
{
    public static async ValueTask<string?> GetPublicUriValidationErrorAsync(
        Uri uri,
        IWebPageAddressResolver resolver,
        CancellationToken cancellationToken = default)
    {
        if (!uri.IsAbsoluteUri)
        {
            return "절대 URL이 필요합니다.";
        }

        if (!uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return "http 또는 https URL만 지원합니다.";
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            return "사용자 정보가 포함된 URL은 지원하지 않습니다.";
        }

        var host = uri.IdnHost;
        if (string.IsNullOrWhiteSpace(host))
        {
            return "호스트 이름이 필요합니다.";
        }

        if (IsLocalHostName(host))
        {
            return "localhost 주소는 읽을 수 없습니다.";
        }

        if (IPAddress.TryParse(uri.Host, out var literalAddress))
        {
            return IsPublicAddress(literalAddress)
                ? null
                : $"공개 주소가 아닌 IP({literalAddress})는 읽을 수 없습니다.";
        }

        if (!host.Contains('.', StringComparison.Ordinal))
        {
            return "공개 DNS 이름으로 보이지 않는 단일 라벨 호스트는 읽을 수 없습니다.";
        }

        IReadOnlyList<IPAddress> addresses;
        try
        {
            addresses = await resolver.GetHostAddressesAsync(host, cancellationToken);
        }
        catch (SocketException e)
        {
            return $"호스트 DNS 조회에 실패했습니다: {e.Message}";
        }

        if (addresses.Count == 0)
        {
            return "호스트 DNS 조회 결과가 없습니다.";
        }

        var blockedAddress = addresses.FirstOrDefault(address => !IsPublicAddress(address));
        if (blockedAddress != null)
        {
            return $"호스트가 공개 주소가 아닌 IP({blockedAddress})로 확인되어 차단했습니다.";
        }

        return null;
    }

    public static async ValueTask<Stream> ConnectToPublicAddressAsync(
        DnsEndPoint endPoint,
        IWebPageAddressResolver resolver,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<IPAddress> addresses;
        if (IPAddress.TryParse(endPoint.Host, out var literalAddress))
        {
            addresses = [literalAddress];
        }
        else
        {
            addresses = await resolver.GetHostAddressesAsync(endPoint.Host, cancellationToken);
        }

        if (addresses.Count == 0)
        {
            throw new HttpRequestException("Host did not resolve to an IP address.");
        }

        var blockedAddress = addresses.FirstOrDefault(address => !IsPublicAddress(address));
        if (blockedAddress != null)
        {
            throw new HttpRequestException($"Host resolved to a non-public IP address: {blockedAddress}");
        }

        Exception? lastException = null;
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, endPoint.Port), cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception e) when (e is SocketException or IOException)
            {
                lastException = e;
                socket.Dispose();
            }
        }

        throw new HttpRequestException("Unable to connect to any resolved public IP address.", lastException);
    }

    public static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address))
        {
            return false;
        }

        return address.AddressFamily switch
        {
            AddressFamily.InterNetwork => IsPublicIPv4Address(address),
            AddressFamily.InterNetworkV6 => IsPublicIPv6Address(address),
            _ => false
        };
    }

    private static bool IsLocalHostName(string host)
    {
        return host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
               || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPublicIPv4Address(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes[0] switch
        {
            0 => false,
            10 => false,
            100 when bytes[1] is >= 64 and <= 127 => false,
            127 => false,
            169 when bytes[1] == 254 => false,
            172 when bytes[1] is >= 16 and <= 31 => false,
            192 when bytes[1] == 168 => false,
            192 when bytes[1] == 0 && bytes[2] is 0 or 2 => false,
            192 when bytes[1] == 88 && bytes[2] == 99 => false,
            198 when bytes[1] is 18 or 19 => false,
            198 when bytes[1] == 51 && bytes[2] == 100 => false,
            203 when bytes[1] == 0 && bytes[2] == 113 => false,
            >= 224 => false,
            _ => true
        };
    }

    private static bool IsPublicIPv6Address(IPAddress address)
    {
        if (address.Equals(IPAddress.IPv6Any)
            || address.Equals(IPAddress.IPv6None)
            || address.IsIPv6LinkLocal
            || address.IsIPv6Multicast
            || address.IsIPv6SiteLocal)
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        if ((bytes[0] & 0xfe) == 0xfc)
        {
            return false;
        }

        return bytes is not [0x20, 0x01, 0x0d, 0xb8, ..];
    }
}
