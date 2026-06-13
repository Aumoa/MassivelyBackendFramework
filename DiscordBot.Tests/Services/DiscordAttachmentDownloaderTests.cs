using System.Net;
using DiscordBot.Options;
using DiscordBot.Services;

namespace DiscordBot.Tests.Services;

public sealed class DiscordAttachmentDownloaderTests
{
    [Fact]
    public async Task DownloadAsync_UsesNamedClientAndReturnsBytes()
    {
        var factory = new StubHttpClientFactory(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([1, 2, 3])
        });
        var downloader = new DiscordAttachmentDownloader(factory);

        var bytes = await downloader.DownloadAsync("https://cdn.discordapp.com/file.txt", 3);

        Assert.Equal(AttachmentDownloadOptions.HttpClientName, factory.RequestedName);
        Assert.Equal([1, 2, 3], bytes);
    }

    [Fact]
    public async Task DownloadAsync_RejectsContentLengthOverLimit()
    {
        var factory = new StubHttpClientFactory(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([1, 2, 3, 4])
        });
        var downloader = new DiscordAttachmentDownloader(factory);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => downloader.DownloadAsync("https://cdn.discordapp.com/file.txt", 3));

        Assert.Contains("exceeding the 3 byte limit", exception.Message);
    }

    [Fact]
    public async Task DownloadAsync_RejectsUnknownLengthBodyOverLimit()
    {
        var factory = new StubHttpClientFactory(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new UnknownLengthByteArrayContent([1, 2, 3, 4])
        });
        var downloader = new DiscordAttachmentDownloader(factory);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => downloader.DownloadAsync("https://cdn.discordapp.com/file.txt", 3));

        Assert.Contains("exceeded the 3 byte limit", exception.Message);
    }

    [Fact]
    public async Task DownloadAsync_ThrowsForHttpErrors()
    {
        var factory = new StubHttpClientFactory(new HttpResponseMessage(HttpStatusCode.NotFound));
        var downloader = new DiscordAttachmentDownloader(factory);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => downloader.DownloadAsync("https://cdn.discordapp.com/missing.txt", 3));
    }

    private sealed class StubHttpClientFactory(HttpResponseMessage response) : IHttpClientFactory
    {
        public string? RequestedName { get; private set; }

        public HttpClient CreateClient(string name)
        {
            RequestedName = name;
            return new HttpClient(new StubHttpMessageHandler(response));
        }
    }

    private sealed class StubHttpMessageHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(response);
        }
    }

    private sealed class UnknownLengthByteArrayContent(byte[] data) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            return stream.WriteAsync(data, 0, data.Length);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
