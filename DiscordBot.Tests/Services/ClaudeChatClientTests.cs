using System.Net;
using AI;
using AI.Providers.Claude;

namespace DiscordBot.Tests.Services;

public sealed class ClaudeChatClientTests
{
    [Fact]
    public async Task GenerateAsync_PreservesHttpStatusCodeOnFailure()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent("overloaded")
            }));
        var client = new ClaudeChatClient(
            httpClient,
            Microsoft.Extensions.Options.Options.Create(new ClaudeChatClientOptions
            {
                ApiKey = "test-key",
                BaseUri = "https://example.test"
            }));

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => client.GenerateAsync(
            "prompt",
            new ChatCompletionOptions { Model = "test-model" }));

        Assert.Equal(HttpStatusCode.TooManyRequests, exception.StatusCode);
        Assert.Contains("Claude API 429", exception.Message);
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
}
