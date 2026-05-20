using System.Text;
using System.Text.Json;
using AI;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace OpenAI.Controllers;

[ApiController]
[Route("api/v1/ollama")]
[Authorize]
public class OllamaProxyController(IChatClient chatClient) : ControllerBase
{
    private static readonly JsonSerializerOptions s_JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    [HttpPost("chat")]
    public async Task PostChatAsync(CancellationToken cancellationToken)
    {
        using var doc = await JsonDocument.ParseAsync(Request.Body, cancellationToken: cancellationToken);
        var root = doc.RootElement;

        var (model, completionOptions) = ReadCompletionOptions(root);
        bool stream = ReadStream(root);
        var messages = ReadMessages(root);

        if (stream)
        {
            await WriteChatStreamAsync(model, completionOptions, messages, cancellationToken);
        }
        else
        {
            await WriteChatSingleAsync(model, completionOptions, messages, cancellationToken);
        }
    }

    [HttpPost("generate")]
    public async Task PostGenerateAsync(CancellationToken cancellationToken)
    {
        using var doc = await JsonDocument.ParseAsync(Request.Body, cancellationToken: cancellationToken);
        var root = doc.RootElement;

        var (model, completionOptions) = ReadCompletionOptions(root);
        bool stream = ReadStream(root);
        string prompt = root.TryGetProperty("prompt", out var p) ? p.GetString() ?? "" : "";
        string? system = root.TryGetProperty("system", out var s) ? s.GetString() : null;

        if (stream)
        {
            await WriteGenerateStreamAsync(model, completionOptions, prompt, system, cancellationToken);
        }
        else
        {
            await WriteGenerateSingleAsync(model, completionOptions, prompt, system, cancellationToken);
        }
    }

    private async Task WriteChatStreamAsync(
        string model,
        ChatCompletionOptions completionOptions,
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken)
    {
        Response.ContentType = "application/x-ndjson";

        await foreach (var chunk in chatClient.ChatAsync(messages, completionOptions, cancellationToken: cancellationToken))
        {
            if (string.IsNullOrEmpty(chunk.Content)) continue;
            await WriteJsonLineAsync(new
            {
                model,
                created_at = DateTimeOffset.UtcNow.ToString("O"),
                message = new { role = "assistant", content = chunk.Content },
                done = false
            }, cancellationToken);
        }

        await WriteJsonLineAsync(new
        {
            model,
            created_at = DateTimeOffset.UtcNow.ToString("O"),
            message = new { role = "assistant", content = "" },
            done = true,
            done_reason = "stop"
        }, cancellationToken);
    }

    private async Task WriteChatSingleAsync(
        string model,
        ChatCompletionOptions completionOptions,
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        await foreach (var chunk in chatClient.ChatAsync(messages, completionOptions, cancellationToken: cancellationToken))
        {
            if (!string.IsNullOrEmpty(chunk.Content))
            {
                sb.Append(chunk.Content);
            }
        }

        Response.ContentType = "application/json";
        await WriteJsonAsync(new
        {
            model,
            created_at = DateTimeOffset.UtcNow.ToString("O"),
            message = new { role = "assistant", content = sb.ToString() },
            done = true,
            done_reason = "stop"
        }, cancellationToken);
    }

    private async Task WriteGenerateStreamAsync(
        string model,
        ChatCompletionOptions completionOptions,
        string prompt,
        string? system,
        CancellationToken cancellationToken)
    {
        Response.ContentType = "application/x-ndjson";

        var messages = BuildGenerateMessages(prompt, system);
        await foreach (var chunk in chatClient.ChatAsync(messages, completionOptions, cancellationToken: cancellationToken))
        {
            if (string.IsNullOrEmpty(chunk.Content)) continue;
            await WriteJsonLineAsync(new
            {
                model,
                created_at = DateTimeOffset.UtcNow.ToString("O"),
                response = chunk.Content,
                done = false
            }, cancellationToken);
        }

        await WriteJsonLineAsync(new
        {
            model,
            created_at = DateTimeOffset.UtcNow.ToString("O"),
            response = "",
            done = true,
            done_reason = "stop"
        }, cancellationToken);
    }

    private async Task WriteGenerateSingleAsync(
        string model,
        ChatCompletionOptions completionOptions,
        string prompt,
        string? system,
        CancellationToken cancellationToken)
    {
        var text = await chatClient.GenerateAsync(prompt, completionOptions, system, cancellationToken);

        Response.ContentType = "application/json";
        await WriteJsonAsync(new
        {
            model,
            created_at = DateTimeOffset.UtcNow.ToString("O"),
            response = text,
            done = true,
            done_reason = "stop"
        }, cancellationToken);
    }

    private async Task WriteJsonLineAsync(object value, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(value, s_JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json + "\n");
        await Response.Body.WriteAsync(bytes, cancellationToken);
        await Response.Body.FlushAsync(cancellationToken);
    }

    private async Task WriteJsonAsync(object value, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(value, s_JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        await Response.Body.WriteAsync(bytes, cancellationToken);
    }

    private static (string Model, ChatCompletionOptions Options) ReadCompletionOptions(JsonElement root)
    {
        if (!root.TryGetProperty("model", out var modelProp) || modelProp.ValueKind != JsonValueKind.String)
        {
            throw new BadHttpRequestException("'model' field is required.");
        }
        var model = modelProp.GetString()!;

        float temperature = 0.7f;
        float? topP = null;
        int? maxTokens = null;

        if (root.TryGetProperty("options", out var opts) && opts.ValueKind == JsonValueKind.Object)
        {
            if (opts.TryGetProperty("temperature", out var t) && t.ValueKind == JsonValueKind.Number)
                temperature = (float)t.GetDouble();
            if (opts.TryGetProperty("top_p", out var p) && p.ValueKind == JsonValueKind.Number)
                topP = (float)p.GetDouble();
            if (opts.TryGetProperty("num_predict", out var n) && n.ValueKind == JsonValueKind.Number)
                maxTokens = n.GetInt32();
        }

        return (model, new ChatCompletionOptions
        {
            Model = model,
            Temperature = temperature,
            TopP = topP,
            MaxTokens = maxTokens
        });
    }

    private static bool ReadStream(JsonElement root)
    {
        // Ollama defaults stream to true when not specified.
        return !root.TryGetProperty("stream", out var s) || s.ValueKind != JsonValueKind.False;
    }

    private static IReadOnlyList<ChatMessage> ReadMessages(JsonElement root)
    {
        var result = new List<ChatMessage>();
        if (!root.TryGetProperty("messages", out var messagesArray) || messagesArray.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var element in messagesArray.EnumerateArray())
        {
            var role = element.TryGetProperty("role", out var r) ? r.GetString() : null;
            var content = element.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "";

            List<ChatImage>? images = null;
            if (element.TryGetProperty("images", out var imgs) && imgs.ValueKind == JsonValueKind.Array)
            {
                images = [];
                foreach (var img in imgs.EnumerateArray())
                {
                    if (img.ValueKind == JsonValueKind.String)
                    {
                        var s = img.GetString();
                        if (!string.IsNullOrEmpty(s)) images.Add(new ChatImage { Base64 = s });
                    }
                }
            }

            var chatRole = role switch
            {
                "system" => ChatRole.System,
                "assistant" => ChatRole.Assistant,
                "tool" => ChatRole.Tool,
                _ => ChatRole.User
            };

            result.Add(new ChatMessage
            {
                Role = chatRole,
                Content = content,
                Images = images
            });
        }

        return result;
    }

    private static IReadOnlyList<ChatMessage> BuildGenerateMessages(string prompt, string? system)
    {
        var list = new List<ChatMessage>();
        if (!string.IsNullOrEmpty(system))
        {
            list.Add(new ChatMessage { Role = ChatRole.System, Content = system });
        }
        list.Add(new ChatMessage { Role = ChatRole.User, Content = prompt });
        return list;
    }
}
