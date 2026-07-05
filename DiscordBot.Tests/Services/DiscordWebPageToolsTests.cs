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
}
