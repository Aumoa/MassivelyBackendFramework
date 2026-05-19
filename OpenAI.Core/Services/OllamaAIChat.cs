using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using AI;

namespace OpenAI.Services;

internal class OllamaAIChat(string conversationTopics, AIModelOptions options, IChatClient chatClient, ToolsProvider tools) : IAIChat
{
    private const string CHAT_SYSTEM_PROMPT =
        "당신은 사용자에게 도움을 주는 친절하고 전문적인 AI 어시스턴트입니다. 당신의 답변은 명확하고 간결하며, 필요할 때 논리적인 구조(목록, 강조 등)를 사용하세요. " +
        "사용자가 별도의 요구를 하지 않는 한, 불필요한 서론이나 사과는 생략하고 즉시 핵심 내용부터 답변하세요. 사용자의 질문 의도를 파악하여 적절한 깊이의 정보를 제공하세요. " +
        "이미지를 생성할 때, 도구 설명에 있는 권장 태그들을 활용하여 사용자의 아이디어를 한 폭의 예술 작품처럼 상세하게 묘사하세요.";

    private const string SUMMARIZE_SYSTEM_PROMPT =
        "당신은 대화 내용을 간결하고 명확하게 요약하는 전문 요약가입니다. 아래 대화 내용을 읽고, 대화의 핵심 주제, 논의된 주요 내용, 그리고 결정된 사항(Action Items)을 중심으로 정리하세요.\n\n" +
        "[요약 규칙]\n" +
        "서론이나 인사말은 생략하고 요약 내용만 출력할 것.\n" +
        "불렛 포인트(Bullet points)를 사용하여 가독성을 높일 것.\n" +
        "대화에 참여한 화자의 관점을 객관적으로 유지할 것.\n" +
        "전체 분량은 원문 대화의 20% 이내로 압축할 것.";

    private const int MAX_HISTORY = 15;

    public string ConversationTopics => conversationTopics;

    public async IAsyncEnumerable<ChunkedResponse> AddChatAsync(
        IReadOnlyList<ChatHistoryMessage> history,
        string message,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var messages = BuildChatMessages(history);
        messages.Add(new ChatMessage { Role = ChatRole.User, Content = message });

        var chatOptions = new ChatCompletionOptions
        {
            Model = options.ChatModel,
            Temperature = 0.4f,
            TopP = 0.9f,
            ContextLength = 32768,
            RepeatPenalty = 1.1f
        };
        var toolFunctions = tools.GetToolFunctions();
        IReadOnlyList<ToolFunctionDescription>? toolList = toolFunctions.Count > 0 ? [.. toolFunctions] : null;

        const int kMaxIterations = 5;
        int iterations = 0;
        while (iterations++ < kMaxIterations)
        {
            List<ChatToolCall> toolCalls = [];
            List<IAsyncEnumerable<(string id, ChunkedResponse? result)>> toolCallsResults = [];
            string content = string.Empty;

            await foreach (var chunk in chatClient.ChatAsync(messages, chatOptions, toolList, cancellationToken))
            {
                if (!string.IsNullOrEmpty(chunk.Content))
                {
                    content += chunk.Content;
                    yield return new ChunkedResponse
                    {
                        Type = ChunkedResponse.Types.Message,
                        Content = chunk.Content
                    };
                }

                if (chunk.ToolCalls != null)
                {
                    foreach (var toolCall in chunk.ToolCalls)
                    {
                        toolCalls.Add(toolCall);
                        toolCallsResults.AddRange(CallAsync(toolCall));
                    }
                }
            }

            if (toolCallsResults.Count > 0)
            {
                messages.Add(new ChatMessage
                {
                    Role = ChatRole.Assistant,
                    Content = content,
                    ToolCalls = [.. toolCalls]
                });

                foreach (var asyncEnumerable in toolCallsResults)
                {
                    await foreach (var (callId, chunkedResponse) in asyncEnumerable)
                    {
                        if (chunkedResponse != null)
                        {
                            yield return chunkedResponse;

                            if (chunkedResponse.Type == ChunkedResponse.Types.ToolResult)
                            {
                                messages.Add(new ChatMessage
                                {
                                    Role = ChatRole.Tool,
                                    Content = chunkedResponse.Content ?? "",
                                    ToolCallId = callId
                                });

                                break;
                            }
                        }
                        else
                        {
                            messages.Add(new ChatMessage
                            {
                                Role = ChatRole.Tool,
                                Content = JsonSerializer.Serialize(new { status = "error" }),
                                ToolCallId = callId
                            });

                            break;
                        }
                    }
                }
            }
            else
            {
                break;
            }
        }

        yield break;

        async IAsyncEnumerable<(string id, ChunkedResponse?)> CallAsync(ChatToolCall call)
        {
            var function = tools.FindFunction(call.FunctionName);
            if (function == null)
            {
                yield return (call.Id, null);
                yield break;
            }

            var arguments = function.BuildArguments(call.Arguments, cancellationToken);

            await foreach (var chunkedResponse in (IAsyncEnumerable<ChunkedResponse>)function.Invocable(arguments))
            {
                if (chunkedResponse.Type == ChunkedResponse.Types.ToolContent)
                {
                    yield return (call.Id, chunkedResponse);
                }
            }
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

        var summaryOptions = new ChatCompletionOptions
        {
            Model = options.ChatModel,
            Temperature = 0.15f,
            TopP = 0.85f,
            MaxTokens = 350
        };

        return await chatClient.GenerateAsync(sb.ToString(), summaryOptions, SUMMARIZE_SYSTEM_PROMPT, cancellationToken);
    }

    private static List<ChatMessage> BuildChatMessages(IReadOnlyList<ChatHistoryMessage> history)
    {
        var messages = new List<ChatMessage>
        {
            new() { Role = ChatRole.System, Content = CHAT_SYSTEM_PROMPT }
        };

        var slice = history.Count > MAX_HISTORY
            ? history.Skip(history.Count - MAX_HISTORY).ToList()
            : (IEnumerable<ChatHistoryMessage>)history;

        foreach (var msg in slice)
        {
            if (msg.Role == MessageRole.Summary)
            {
                messages.Add(new ChatMessage { Role = ChatRole.User, Content = "[이전 대화 요약]\n" + msg.Content });
                messages.Add(new ChatMessage { Role = ChatRole.Assistant, Content = "이전 대화 내용을 확인했습니다." });
            }
            else
            {
                messages.Add(new ChatMessage
                {
                    Role = msg.Role == MessageRole.User ? ChatRole.User : ChatRole.Assistant,
                    Content = msg.Content
                });
            }
        }

        return messages;
    }
}
