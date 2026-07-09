using System.Net;
using System.Net.Http;
using OAuth2.Services;

namespace OAuth2.Controllers.Tests;

public sealed class AccountPictureRemoteConnectionTests
{
    [Fact]
    public async Task ConnectToPublicAddressAsync_RejectsPrivateResolvedAddress()
    {
        var endpoint = new DnsEndPoint("profile.example.test", 443);
        var connected = false;

        await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await AccountPictureRemoteConnection.ConnectToPublicAddressAsync(
                endpoint,
                static (_, _) => ValueTask.FromResult(new[] { IPAddress.Loopback }),
                (_, _, _) =>
                {
                    connected = true;
                    return ValueTask.FromResult<Stream>(new MemoryStream());
                },
                CancellationToken.None);
        });

        Assert.False(connected);
    }

    [Fact]
    public async Task ConnectToPublicAddressAsync_ConnectsToResolvedPublicAddress()
    {
        var endpoint = new DnsEndPoint("profile.example.test", 443);
        var publicAddress = IPAddress.Parse("93.184.216.34");
        IPAddress? connectedAddress = null;
        var connectedPort = 0;

        await using var stream = await AccountPictureRemoteConnection.ConnectToPublicAddressAsync(
            endpoint,
            (_, _) => ValueTask.FromResult(new[] { publicAddress }),
            (address, port, _) =>
            {
                connectedAddress = address;
                connectedPort = port;
                return ValueTask.FromResult<Stream>(new MemoryStream());
            },
            CancellationToken.None);

        Assert.NotNull(stream);
        Assert.Equal(publicAddress, connectedAddress);
        Assert.Equal(443, connectedPort);
    }

    [Fact]
    public async Task ConnectToPublicAddressAsync_RejectsMixedPublicAndPrivateAddresses()
    {
        var endpoint = new DnsEndPoint("profile.example.test", 443);
        var connected = false;

        await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await AccountPictureRemoteConnection.ConnectToPublicAddressAsync(
                endpoint,
                static (_, _) => ValueTask.FromResult(new[] { IPAddress.Parse("93.184.216.34"), IPAddress.Loopback }),
                (_, _, _) =>
                {
                    connected = true;
                    return ValueTask.FromResult<Stream>(new MemoryStream());
                },
                CancellationToken.None);
        });

        Assert.False(connected);
    }
}
