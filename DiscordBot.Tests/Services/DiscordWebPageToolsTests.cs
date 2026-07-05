using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using DiscordBot.Options;
using DiscordBot.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiscordBot.Tests.Services;

public sealed class DiscordWebPageToolsTests
{
    [Fact]
    public async Task ReadStaticWebPageAsync_UsesNamedClientAndReturnsReadableHtmlWithLimitNotice()
    {
        var factory = new StubHttpClientFactory(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                <html>
                <head><title>Example Title</title><style>.hidden { display: none; }</style></head>
                <body><script>alert('nope')</script><h1>Hello &amp; welcome</h1><p>First paragraph.</p></body>
                </html>
                """,
                Encoding.UTF8,
                "text/html")
        });
        var tool = CreateTool(factory);

        var result = await tool.ReadStaticWebPageAsync("https://example.com/page");

        Assert.Equal(WebPageReadOptions.HttpClientName, factory.RequestedName);
        Assert.Contains("정적 웹페이지 텍스트 읽기 결과", result);
        Assert.Contains("Fetch-Mode: static HTTP GET; JavaScript was not executed.", result);
        Assert.Contains("가져온 정적 텍스트 기준", result);
        Assert.Contains("Source: https://example.com/page", result);
        Assert.Contains("Example Title", result);
        Assert.Contains("Hello & welcome", result);
        Assert.Contains("First paragraph.", result);
        Assert.DoesNotContain("alert", result);
        Assert.DoesNotContain("display: none", result);
    }

    [Fact]
    public async Task ReadStaticWebPageAsync_RejectsPrivateResolvedAddressBeforeHttpRequest()
    {
        var factory = new StubHttpClientFactory();
        var resolver = new StubWebPageAddressResolver();
        resolver.Set("example.com", IPAddress.Parse("10.0.0.5"));
        var tool = CreateTool(factory, resolver);

        var result = await tool.ReadStaticWebPageAsync("https://example.com/private");

        Assert.Contains("정적 웹페이지 텍스트를 읽지 못했습니다", result);
        Assert.Contains("공개 주소가 아닌 IP", result);
        Assert.Empty(factory.Requests);
    }

    [Theory]
    [InlineData("https://localhost/admin")]
    [InlineData("https://service.localhost/admin")]
    [InlineData("https://intranet/admin")]
    public async Task ReadStaticWebPageAsync_RejectsLocalhostAndSingleLabelHostsBeforeHttpRequest(string url)
    {
        var factory = new StubHttpClientFactory();
        var tool = CreateTool(factory);

        var result = await tool.ReadStaticWebPageAsync(url);

        Assert.Contains("정적 웹페이지 텍스트를 읽지 못했습니다", result);
        Assert.Empty(factory.Requests);
    }

    [Fact]
    public async Task ReadStaticWebPageAsync_RejectsRedirectToPrivateAddress()
    {
        var response = new HttpResponseMessage(HttpStatusCode.Found);
        response.Headers.Location = new Uri("http://127.0.0.1/admin");
        var factory = new StubHttpClientFactory(response);
        var tool = CreateTool(factory);

        var result = await tool.ReadStaticWebPageAsync("https://example.com/start");

        Assert.Contains("127.0.0.1", result);
        Assert.Single(factory.Requests);
    }

    [Fact]
    public async Task ReadStaticWebPageAsync_RevalidatesRedirectHostDnsBeforeSecondRequest()
    {
        var response = new HttpResponseMessage(HttpStatusCode.Found);
        response.Headers.Location = new Uri("https://private.example/admin");
        var factory = new StubHttpClientFactory(response);
        var resolver = new StubWebPageAddressResolver();
        resolver.Set("private.example", IPAddress.Parse("192.168.1.10"));
        var tool = CreateTool(factory, resolver);

        var result = await tool.ReadStaticWebPageAsync("https://example.com/start");

        Assert.Contains("192.168.1.10", result);
        Assert.Single(factory.Requests);
    }

    [Fact]
    public async Task ReadStaticWebPageAsync_RejectsNonTextContentType()
    {
        var content = new ByteArrayContent([1, 2, 3]);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        var factory = new StubHttpClientFactory(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = content
        });
        var tool = CreateTool(factory);

        var result = await tool.ReadStaticWebPageAsync("https://example.com/image.png");

        Assert.Contains("텍스트/HTML 콘텐츠로 보이지 않습니다", result);
        Assert.Contains("image/png", result);
    }

    [Fact]
    public async Task ReadStaticWebPageAsync_TruncatesToRequestedCharacterLimit()
    {
        var text = new string('a', 1500);
        var factory = new StubHttpClientFactory(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(text, Encoding.UTF8, "text/plain")
        });
        var tool = CreateTool(factory);

        var result = await tool.ReadStaticWebPageAsync("https://example.com/long", max_characters: 1000);

        Assert.Contains("truncated to 1000", result);
        Assert.Contains("...(truncated)", result);
    }

    [Fact]
    public async Task ReadStaticWebPageAsync_TimesOutSlowResponseBody()
    {
        var factory = new StubHttpClientFactory(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new BlockingReadStream())
            {
                Headers =
                {
                    ContentType = new MediaTypeHeaderValue("text/plain")
                }
            }
        });
        var tool = CreateTool(factory, options: new WebPageReadOptions { TimeoutSeconds = 1 });
        var stopwatch = Stopwatch.StartNew();

        var result = await tool.ReadStaticWebPageAsync("https://example.com/slow");

        Assert.Contains("요청 시간이 초과", result);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData("0.0.0.1")]
    [InlineData("10.0.0.1")]
    [InlineData("100.64.0.1")]
    [InlineData("127.0.0.1")]
    [InlineData("169.254.1.1")]
    [InlineData("172.16.0.1")]
    [InlineData("192.168.1.1")]
    [InlineData("192.0.0.1")]
    [InlineData("192.0.2.1")]
    [InlineData("198.51.100.1")]
    [InlineData("203.0.113.1")]
    [InlineData("224.0.0.1")]
    [InlineData("::")]
    [InlineData("::1")]
    [InlineData("fe80::1")]
    [InlineData("fc00::1")]
    [InlineData("2001:db8::1")]
    [InlineData("ff02::1")]
    public void IsPublicAddress_RejectsRepresentativeBlockedRanges(string address)
    {
        Assert.False(WebPageAddressSafety.IsPublicAddress(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("93.184.216.34")]
    [InlineData("2606:4700:4700::1111")]
    public void IsPublicAddress_AllowsRepresentativePublicAddresses(string address)
    {
        Assert.True(WebPageAddressSafety.IsPublicAddress(IPAddress.Parse(address)));
    }

    [Fact]
    public async Task ConnectToPublicAddressAsync_RejectsPrivateResolvedAddressBeforeSocketConnect()
    {
        var resolver = new StubWebPageAddressResolver();
        resolver.Set("private.example", IPAddress.Parse("10.0.0.7"));

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            async () => await WebPageAddressSafety.ConnectToPublicAddressAsync(
                new DnsEndPoint("private.example", 443),
                resolver));

        Assert.Contains("non-public IP", exception.Message);
    }

    private static DiscordWebPageTools CreateTool(
        StubHttpClientFactory factory,
        StubWebPageAddressResolver? resolver = null,
        WebPageReadOptions? options = null)
    {
        return new DiscordWebPageTools(
            factory,
            resolver ?? new StubWebPageAddressResolver(),
            Microsoft.Extensions.Options.Options.Create(options ?? new WebPageReadOptions()),
            NullLogger<DiscordWebPageTools>.Instance);
    }

    private sealed class StubWebPageAddressResolver : IWebPageAddressResolver
    {
        private readonly Dictionary<string, IReadOnlyList<IPAddress>> m_Addresses = new(StringComparer.OrdinalIgnoreCase)
        {
            ["example.com"] = [IPAddress.Parse("93.184.216.34")]
        };

        public void Set(string host, params IPAddress[] addresses)
        {
            m_Addresses[host] = addresses;
        }

        public ValueTask<IReadOnlyList<IPAddress>> GetHostAddressesAsync(
            string host,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(
                m_Addresses.TryGetValue(host, out var addresses)
                    ? addresses
                    : [IPAddress.Parse("93.184.216.34")]);
        }
    }

    private sealed class StubHttpClientFactory(params HttpResponseMessage[] responses) : IHttpClientFactory
    {
        private readonly Queue<HttpResponseMessage> m_Responses = new(responses);

        public string? RequestedName { get; private set; }

        public List<Uri> Requests { get; } = [];

        public HttpClient CreateClient(string name)
        {
            RequestedName = name;
            return new HttpClient(new StubHttpMessageHandler(Requests, m_Responses));
        }
    }

    private sealed class StubHttpMessageHandler(
        List<Uri> requests,
        Queue<HttpResponseMessage> responses) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            requests.Add(request.RequestUri!);
            return Task.FromResult(responses.Count == 0
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(string.Empty, Encoding.UTF8, "text/plain")
                }
                : responses.Dequeue());
        }
    }

    private sealed class BlockingReadStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }
}
