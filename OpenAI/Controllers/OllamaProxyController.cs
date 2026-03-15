using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace OpenAI.Controllers;

[ApiController]
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

    private bool IsAuthorized()
    {
        if (HttpContext.User.Identity?.IsAuthenticated == true)
        {
            return true;
        }

        var token = ExtractToken();
        var apiKey = ollamaOptions.Value.AdminApiKey;
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(apiKey))
        {
            return false;
        }

        var tokenBytes = Encoding.UTF8.GetBytes(token);
        var keyBytes = Encoding.UTF8.GetBytes(apiKey);
        return CryptographicOperations.FixedTimeEquals(tokenBytes, keyBytes);
    }

    private string? ExtractToken()
    {
        var authHeader = Request.Headers.Authorization.ToString();
        if (!string.IsNullOrEmpty(authHeader))
        {
            return authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? authHeader["Bearer ".Length..]
                : authHeader;
        }

        return Request.Query["access_token"].FirstOrDefault();
    }

    private async Task ProxyToOllamaAsync(string path, CancellationToken cancellationToken)
    {
        if (!IsAuthorized())
        {
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            await Response.WriteAsJsonAsync(new { error = "unauthorized" }, cancellationToken);
            return;
        }

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
