using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace OpenAI.Controllers;

[ApiController]
[Authorize]
public class OllamaProxyController(IOptions<OllamaOptions> ollamaOptions, IHttpClientFactory httpClientFactory) : ControllerBase
{
    [HttpPost("/api/chat")]
    public async Task PostChatAsync(CancellationToken cancellationToken)
    {
        await ProxyToOllamaAsync("/api/chat", cancellationToken);
    }

    [HttpPost("/api/generate")]
    public async Task PostGenerateAsync(CancellationToken cancellationToken)
    {
        await ProxyToOllamaAsync("/api/generate", cancellationToken);
    }

    private async Task ProxyToOllamaAsync(string path, CancellationToken cancellationToken)
    {
        var baseUri = new Uri(ollamaOptions.Value.Uri.TrimEnd('/') + "/");
        var targetUri = new Uri(baseUri, path.TrimStart('/'));
        var client = httpClientFactory.CreateClient();

        using var upstreamRequest = new HttpRequestMessage(HttpMethod.Post, targetUri)
        {
            Content = new StreamContent(Request.Body)
            {
                Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json") }
            }
        };

        using var upstreamResponse = await client.SendAsync(
            upstreamRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        Response.StatusCode = (int)upstreamResponse.StatusCode;

        var contentType = upstreamResponse.Content.Headers.ContentType?.ToString();
        if (!string.IsNullOrEmpty(contentType))
        {
            Response.ContentType = contentType;
        }

        await using var upstream = await upstreamResponse.Content.ReadAsStreamAsync(cancellationToken);
        await upstream.CopyToAsync(Response.Body, cancellationToken);
    }
}
