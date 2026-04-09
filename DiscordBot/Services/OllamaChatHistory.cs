using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AI;
using Discord;

namespace DiscordBot.Services;

public class OllamaChatHistory(ILogger logger, OllamaService.Configuration options, HttpClient http)
{
    private record FunctionCall
    {
        [JsonPropertyName("name")]
        public required string Name { get; set; }

        [JsonPropertyName("arguments")]
        public required JsonElement Arguments { get; set; }
    }

    private record ToolCall
    {
        [JsonPropertyName("id")]
        public required string Id { get; set; }

        [JsonPropertyName("function")]
        public required FunctionCall Function { get; set; }
    }

    private record ChatMessage
    {
        [JsonPropertyName("role")]
        public required string Role { get; set; }

        [JsonPropertyName("content")]
        public required string Content { get; set; }

        [JsonPropertyName("thinking")]
        public string Thinking { get; set; } = "";

        [JsonPropertyName("images")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string[]? Images { get; set; }

        [JsonPropertyName("tool_calls")]
        public ToolCall[] ToolCalls { get; set; } = [];

        [JsonPropertyName("tool_call_id")]
        public string? ToolCallId { get; set; }
    }

    private record OllamaChatResponse
    {
        [JsonPropertyName("message")]
        public required ChatMessage Message { get; set; }
    }

    private record OllamaGenerateResponse
    {
        [JsonPropertyName("response")]
        public required string Response { get; set; }
    }

    private readonly List<ChatMessage> m_Messages = [];
    private readonly SemaphoreSlim m_Semaphore = new(1);

    private static object[] CreateOllamaTools(IReadOnlyCollection<ToolFunctionDescription> functions)
    {
        return [.. functions.Select(f => new
        {
            type = "function",
            function = new
            {
                name = f.Name,
                description = f.Description,
                parameters = new
                {
                    type = "object",
                    properties = f.Parameters.ToDictionary(
                        p => p.Name,
                        p => (object)new
                        {
                            type = p.Type switch
                            {
                                ToolFunctionDescription.SimpleType.String => "string",
                                ToolFunctionDescription.SimpleType.Number => "number",
                                ToolFunctionDescription.SimpleType.Integer => "integer",
                                ToolFunctionDescription.SimpleType.Boolean => "boolean",
                                _ => "string"
                            },
                            description = p.Description
                        }),
                    required = f.Parameters.Where(p => p.IsRequired).Select(p => p.Name).ToArray()
                }
            }
        })];
    }

    public async IAsyncEnumerable<ChatResponseChunk> AddAsync(IUser author, string prompt, ToolsProvider toolsProvider, IReadOnlyList<string>? images = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (author.IsBot)
        {
            yield break;
        }

        await m_Semaphore.WaitAsync(cancellationToken);
        try
        {
            IEnumerable<ChatMessage> recentHistory = [];
            if (!string.IsNullOrWhiteSpace(options.Persona))
            {
                recentHistory = recentHistory.Append(new ChatMessage
                {
                    Role = "system",
                    Content = options.Persona,
                    Thinking = ""
                });
            }

            recentHistory = recentHistory.Concat(m_Messages);
            var userMessage = new ChatMessage
            {
                Role = "user",
                Content = $"[{DateTimeOffset.UtcNow}]({author.Username}님의 메시지): {prompt}",
                Thinking = "",
                Images = images?.Count > 0 ? [.. images] : null
            };

            IAsyncEnumerable<ChatResponseChunk> messages;
            List<ToolCall> toolCalls = [];
            List<ChatMessage> messagesAppend = [userMessage];

            while (true)
            {
                var ollamaTools = CreateOllamaTools(toolsProvider.GetToolFunctions());
                var json = JsonSerializer.Serialize(new
                {
                    model = options.Model,
                    messages = recentHistory.Concat(messagesAppend),
                    tools = ollamaTools,
                    stream = true,
                    keep_alive = options.KeepAlive,
                    options = new
                    {
                        num_ctx = 8192,
                        temperature = 0.7
                    }
                });

                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                try
                {
                    var request = new HttpRequestMessage(HttpMethod.Post, options.Uri + "/api/chat")
                    {
                        Content = content
                    };
                    var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                    messages = HandleResponseChunksAsync(stream, toolCalls, cancellationToken);
                }
                catch (Exception ex)
                {
                    messages = Default($"응답을 생성하는 중 오류가 발생했습니다: {ex.Message}");
                }

                await foreach (var message in messages)
                {
                    yield return message;
                }

                if (toolCalls.Count > 0)
                {
                    try
                    {
                        messagesAppend.Add(new ChatMessage
                        {
                            Role = "assistant",
                            Content = "",
                            ToolCalls = [.. toolCalls]
                        });

                        foreach (var toolCall in toolCalls)
                        {
                            yield return new ChatResponseChunk
                            {
                                Content = "",
                                Thinking = "",
                                ToolName = toolCall.Function.Name
                            };

                            var function = toolsProvider.FindFunction(toolCall.Function.Name);
                            if (function != null)
                            {
                                var args = function.BuildArguments(toolCall.Function.Arguments, cancellationToken);
                                var result = function.Invocable(args);
                                string toolResult;

                                if (result is Task<string> taskResult)
                                    toolResult = await taskResult;
                                else
                                    toolResult = result?.ToString() ?? string.Empty;

                                messagesAppend.Add(new ChatMessage
                                {
                                    Role = "tool",
                                    Content = JsonSerializer.Serialize(new { status = "success", content = toolResult }),
                                    ToolCallId = toolCall.Id
                                });
                            }
                            else
                            {
                                messagesAppend.Add(new ChatMessage
                                {
                                    Role = "tool",
                                    Content = JsonSerializer.Serialize(new { status = "error", reason = "tool_not_exists" }),
                                    ToolCallId = toolCall.Id
                                });
                            }
                        }
                    }
                    finally
                    {
                        toolCalls.Clear();
                    }

                    continue;
                }
                else
                {
                    break;
                }
            }

            m_Messages.AddRange(messagesAppend);

            if (m_Messages.Count >= options.MemorySize)
            {
                int summaryRange = m_Messages.Count - 2;
                var summaryContent = string.Join("\n", m_Messages.Take(summaryRange).Select(m => $"({m.Role}): {m.Content}"));
                var summarySystem = $@"
너는 대화 요약 전문가야. 아래 내용을 참고해서 사용자가 원하는 요약을 진행해주어야 해.

[시스템 정보]
- 페르소나: {options.Persona}
- 현재 시간: {DateTimeOffset.UtcNow}
- 지시사항: 

[데이터 형식 규칙]
- 대화는 'Role: (작성자님의 메시지): 내용' 형태야.
- 예시: 'user: (liberty님의 메시지): 안녕'은 사용자 liberty가 보낸 메시지야.
- 예시: 'assistant: 내용'은 너(AI)의 이전 답변이야.
- [이전 대화 요약]: 으로 시작하는 대화는 이전에 네가 먼저 요약한 내용이야.
";
                var summaryPrompt = $@"
아래 대화 내용을 핵심 사건과 사용자 성향 위주로 간결하게 요약해.

[대화 내용]
{summaryContent}
";

                var response = await http.PostAsJsonAsync(options.Uri + "/api/generate", new
                {
                    model = options.SummaryModel,
                    system = summarySystem,
                    stream = false,
                    prompt = summaryPrompt,
                    keep_alive = options.SummaryKeepAlive,
                    options = new
                    {
                        temperature = 0.5
                    }
                }, cancellationToken);
                var result = await response.Content.ReadFromJsonAsync<OllamaGenerateResponse>(cancellationToken);
                m_Messages.RemoveRange(0, summaryRange);
                if (result != null)
                {
                    m_Messages.Insert(0, new ChatMessage
                    {
                        Role = "system",
                        Content = $"[이전 대화 요약]: {result.Response}",
                        Thinking = ""
                    });

                    if (logger.IsEnabled(LogLevel.Debug))
                    {
                        logger.LogDebug("The {count} messages were summarized as follows: {summary}", summaryRange, result.Response);
                    }
                }
                else
                {
                    logger.LogError("Failed to generate summary message.");
                }
            }

            yield break;

            async IAsyncEnumerable<ChatResponseChunk> HandleResponseChunksAsync(Stream stream, List<ToolCall> toolCalls, [EnumeratorCancellation] CancellationToken cancellationToken)
            {
                try
                {
                    using var reader = new StreamReader(stream);
                    string role = string.Empty;
                    string content = string.Empty;
                    string thinking = string.Empty;

                    while (true)
                    {
                        var line = await reader.ReadLineAsync(cancellationToken);
                        if (line == null)
                        {
                            break;
                        }

                        if (string.IsNullOrEmpty(line))
                        {
                            continue;
                        }

                        var chunk = JsonSerializer.Deserialize<OllamaChatResponse>(line);
                        if (chunk == null)
                        {
                            yield return new ChatResponseChunk
                            {
                                Content = "Failed to deserialize ollama response.",
                                Thinking = ""
                            };

                            yield break;
                        }

                        role = chunk.Message.Role;
                        content += chunk.Message.Content;
                        thinking += chunk.Message.Thinking;

                        if (!string.IsNullOrEmpty(chunk.Message.Content) || !string.IsNullOrEmpty(chunk.Message.Thinking))
                        {
                            yield return new ChatResponseChunk
                            {
                                Content = chunk.Message.Content,
                                Thinking = chunk.Message.Thinking
                            };
                        }

                        if (chunk.Message.ToolCalls.Length > 0)
                        {
                            toolCalls.AddRange(chunk.Message.ToolCalls);
                        }
                    }

                    var responseMessage = new ChatMessage
                    {
                        Role = role,
                        Content = content,
                        Thinking = thinking
                    };

                    messagesAppend.Add(responseMessage);
                }
                finally
                {
                    await stream.DisposeAsync();
                }
            }
        }
        finally
        {
            m_Semaphore.Release();
        }

        static async IAsyncEnumerable<ChatResponseChunk> Default(string message)
        {
            yield return new ChatResponseChunk
            {
                Content = message,
                Thinking = ""
            };
        }
    }

    public async Task TrySummarizeAsync(CancellationToken cancellationToken = default)
    {
        await m_Semaphore.WaitAsync(cancellationToken);
        try
        {
            if (m_Messages.Count >= options.MemorySize)
            {
                logger.LogInformation("Trying summarize chat history.");

                int summaryRange = m_Messages.Count - 2;
                var summaryContent = string.Join("\n", m_Messages.Take(summaryRange).Select(m => $"({m.Role}): {m.Content}"));
                var summarySystem = $@"
너는 대화 요약 전문가야. 아래 내용을 참고해서 사용자가 원하는 요약을 진행해주어야 해.

[시스템 정보]
- 페르소나: {options.Persona}
- 현재 시간: {DateTime.Now:yyyy-MM-dd HH:mm}
- 지시사항: 

[데이터 형식 규칙]
- 대화는 'Role: (작성자님의 메시지): 내용' 형태야.
- 예시: 'user: (liberty님의 메시지): 안녕'은 사용자 liberty가 보낸 메시지야.
- 예시: 'assistant: 내용'은 너(AI)의 이전 답변이야.
- [이전 대화 요약]: 으로 시작하는 대화는 이전에 네가 먼저 요약한 내용이야.
";
                var summaryPrompt = $@"
아래 대화 내용을 핵심 사건과 사용자 성향 위주로 간결하게 요약해.

[대화 내용]
{summaryContent}
";

                var response = await http.PostAsJsonAsync(options.Uri + "/api/generate", new
                {
                    model = options.SummaryModel,
                    system = summarySystem,
                    stream = false,
                    prompt = summaryPrompt,
                    options = new
                    {
                        temperature = 0.5
                    }
                }, cancellationToken);
                var result = await response.Content.ReadFromJsonAsync<OllamaGenerateResponse>(cancellationToken);
                m_Messages.RemoveRange(0, summaryRange);
                if (result != null)
                {
                    m_Messages.Insert(0, new ChatMessage
                    {
                        Role = "system",
                        Content = $"[이전 대화 요약]: {result.Response}",
                        Thinking = ""
                    });

                    if (logger.IsEnabled(LogLevel.Debug))
                    {
                        logger.LogDebug("The {count} messages were summarized as follows: {summary}", summaryRange, result.Response);
                    }
                }
                else
                {
                    logger.LogError("Failed to generate summary message.");
                }
            }
        }
        finally
        {
            m_Semaphore.Release();
        }
    }
}
