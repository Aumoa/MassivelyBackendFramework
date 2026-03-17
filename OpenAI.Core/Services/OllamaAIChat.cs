using System.Collections.Generic;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Google.Protobuf;
using OpenAI.Ollama;
using OpenAI.Tools;

namespace OpenAI.Services;

internal class OllamaAIChat(string conversationTopics, OllamaOptions options, HttpClient http, ToolsProvider tools) : IAIChat
{
    private const string CHAT_SYSTEM_PROMPT =
        "당신은 사용자에게 도움을 주는 친절하고 전문적인 AI 어시스턴트입니다. 당신의 답변은 명확하고 간결하며, 필요할 때 논리적인 구조(목록, 강조 등)를 사용하세요. " +
        "사용자가 별도의 요구를 하지 않는 한, 불필요한 서론이나 사과는 생략하고 즉시 핵심 내용부터 답변하세요. 사용자의 질문 의도를 파악하여 적절한 깊이의 정보를 제공하세요.";

    private const string SUMMARIZE_SYSTEM_PROMPT =
        "당신은 대화 내용을 간결하고 명확하게 요약하는 전문 요약가입니다. 아래 대화 내용을 읽고, 대화의 핵심 주제, 논의된 주요 내용, 그리고 결정된 사항(Action Items)을 중심으로 정리하세요.\n\n" +
        "[요약 규칙]\n" +
        "서론이나 인사말은 생략하고 요약 내용만 출력할 것.\n" +
        "불렛 포인트(Bullet points)를 사용하여 가독성을 높일 것.\n" +
        "대화에 참여한 화자의 관점을 객관적으로 유지할 것.\n" +
        "전체 분량은 원문 대화의 20% 이내로 압축할 것.";

    private const int MAX_HISTORY = 15;

    public string ConversationTopics => conversationTopics;

    public async IAsyncEnumerable<string> AddChatAsync(
        IReadOnlyList<ChatHistoryMessage> history,
        string message,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var messages = BuildChatMessages(history);
        messages.Add(new OllamaChatMessage { Role = "user", Content = message });

        const int kMaxIterations = 5;
        int iterations = 0;
        while (iterations++ < kMaxIterations)
        {
            var request = new OllamaChatRequest
            {
                Model = options.ChatModel,
                Messages = [.. messages],
                Stream = true,
                Tools = OllamaChatRequest.CreateFrom(tools.GetToolFunctions()),
                Options = new OllamaChatOptions
                {
                    Temperature = 0.4,
                    TopP = 0.9,
                    NumCtx = 32768,
                    RepeatPenalty = 1.1
                }
            };

            var inputContent = JsonContent.Create(request);
            var requestMessage = new HttpRequestMessage(HttpMethod.Post, options.Uri + "/api/chat")
            {
                Content = inputContent
            };

            using var response = await http.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream);

            List<OllamaToolCall> toolCalls = [];
            List<Task<(string id, object? result)>> toolCallsResults = [];

            string? line;
            while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var chunk = JsonSerializer.Deserialize<OllamaChatResponse>(line);
                if (chunk != null)
                {
                    if (chunk.Message != null)
                    {
                        if (chunk.Message.Content is { Length: > 0 } content)
                        {
                            yield return content;
                        }

                        if (chunk.Message.ToolCalls != null)
                        {
                            foreach (var toolCall in chunk.Message.ToolCalls)
                            {
                                toolCalls.Add(toolCall);
                                toolCallsResults.AddRange(CallAsync(toolCall));
                            }
                        }
                    }

                    if (chunk.Done == true)
                    {
                        break;
                    }
                }
            }

            if (toolCallsResults.Count > 0)
            {
                var toolResults = await Task.WhenAll(toolCallsResults);
                messages.Add(new OllamaChatMessage
                {
                    Role = "assistant",
                    ToolCalls = [.. toolCalls]
                });
                foreach (var (id, result) in toolResults)
                {
                    messages.Add(new OllamaChatMessage
                    {
                        Role = "tool",
                        Content = JsonSerializer.Serialize(result),
                        ToolCallId = id
                    });
                }
            }
            else
            {
                break;
            }
        }

        yield break;

        async Task<(string id, object? result)> CallAsync(OllamaToolCall call)
        {
            var function = tools.FindFunction(call.Function.Name);
            if (function == null)
            {
                return (call.Id, null);
            }

            int count = function.Parameters.Length;
            object?[] arguments = new object[count + (function.HasCancellationTokenParameter ? 1 : 0)];
            for (int i = 0; i < count; ++i)
            {
                if (call.Function.Arguments.TryGetProperty(function.Parameters[i].Name, out var argument))
                {
                    switch (function.Parameters[i].Type)
                    {
                        case ToolFunctionDescription.SimpleType.String:
                            arguments[i] = argument.GetString();
                            break;
                        case ToolFunctionDescription.SimpleType.Number:
                            arguments[i] = argument.GetDouble();
                            break;
                        case ToolFunctionDescription.SimpleType.Boolean:
                            arguments[i] = argument.GetBoolean();
                            break;
                    }
                }
            }

            if (function.HasCancellationTokenParameter)
            {
                arguments[^1] = cancellationToken;
            }

            var result = await function.Invocable(arguments);
            return (call.Id, result);
        }
    }

    public async ValueTask<string> SummarizeAsync(
        IReadOnlyList<ChatHistoryMessage> messages,
        CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder();
        foreach (var msg in messages)
        {
            var speaker = msg.Role switch
            {
                MessageRole.User => "사용자",
                MessageRole.Assistant => "AI",
                MessageRole.Summary => "[이전 요약]",
                _ => "알 수 없음"
            };
            sb.AppendLine($"{speaker}: {msg.Content}");
            sb.AppendLine();
        }

        var response = await http.PostAsJsonAsync(options.Uri + "/api/generate", new
        {
            model = options.ChatModel,
            prompt = sb.ToString(),
            stream = false,
            system = SUMMARIZE_SYSTEM_PROMPT,
            options = new
            {
                temperature = 0.15,
                top_p = 0.85,
                num_predict = 350
            }
        }, cancellationToken);
        response.EnsureSuccessStatusCode();

        var generateResponse = await response.Content.ReadFromJsonAsync<OllamaGenerateResponse>(cancellationToken)
            ?? throw new InvalidOperationException("Failed to deserialize the summarization response from Ollama API.");

        return generateResponse.Response;
    }

    private static List<OllamaChatMessage> BuildChatMessages(IReadOnlyList<ChatHistoryMessage> history)
    {
        var messages = new List<OllamaChatMessage>
        {
            new OllamaChatMessage { Role = "system", Content = CHAT_SYSTEM_PROMPT }
        };

        // Take the last MAX_HISTORY entries
        var slice = history.Count > MAX_HISTORY
            ? history.Skip(history.Count - MAX_HISTORY).ToList()
            : (IEnumerable<ChatHistoryMessage>)history;

        foreach (var msg in slice)
        {
            if (msg.Role == MessageRole.Summary)
            {
                // Inject summary as a user/assistant exchange so the model has context
                messages.Add(new OllamaChatMessage { Role = "user", Content = "[이전 대화 요약]\n" + msg.Content });
                messages.Add(new OllamaChatMessage { Role = "assistant", Content = "이전 대화 내용을 확인했습니다." });
            }
            else
            {
                messages.Add(new OllamaChatMessage
                {
                    Role = msg.Role == MessageRole.User ? "user" : "assistant",
                    Content = msg.Content
                });
            }
        }

        return messages;
    }
}
