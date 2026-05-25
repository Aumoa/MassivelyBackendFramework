using System.Runtime.CompilerServices;
using System.Text.Json;
using AI;
using Discord;

namespace DiscordBot.Services;

public class OllamaChatHistory(
    ILogger logger,
    OllamaService.Configuration options,
    IChatClient chatClient,
    IClaudeSettingsService claudeSettings)
{
    private readonly List<ChatMessage> m_Messages = [];
    private readonly SemaphoreSlim m_Semaphore = new(1);

    public async IAsyncEnumerable<ChatResponseChunk> AddAsync(IUser author, string prompt, ToolsProvider toolsProvider, IReadOnlyList<ChatImage>? images = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (author.IsBot)
        {
            yield break;
        }

        await m_Semaphore.WaitAsync(cancellationToken);
        try
        {
            List<ChatMessage> recentHistory = [];
            if (!string.IsNullOrWhiteSpace(options.Persona))
            {
                recentHistory.Add(new ChatMessage
                {
                    Role = ChatRole.System,
                    Content = options.Persona
                });
            }

            PruneRememberedMessages();
            recentHistory.AddRange(m_Messages);

            var userMessage = new ChatMessage
            {
                Role = ChatRole.User,
                Content = $"[{DateTimeOffset.UtcNow}]({author.Username}님의 메시지): {prompt}",
                Images = images
            };

            List<ChatToolCall> toolCalls = [];
            List<ChatMessage> messagesAppend = [userMessage];

            while (true)
            {
                var settings = await claudeSettings.GetAsync(cancellationToken);
                var allMessages = recentHistory.Concat(messagesAppend).ToList();
                var chatOptions = new ChatCompletionOptions
                {
                    Model = settings.Model,
                    Temperature = 0.7f,
                    MaxTokens = settings.DefaultMaxTokens,
                    ContextLength = 8192
                };
                var toolFunctions = toolsProvider.GetToolFunctions();
                IReadOnlyList<ToolFunctionDescription>? toolList = toolFunctions.Count > 0 ? [.. toolFunctions] : null;

                string content = string.Empty;

                await foreach (var chunk in chatClient.ChatAsync(allMessages, chatOptions, toolList, cancellationToken))
                {
                    content += chunk.Content;

                    if (!string.IsNullOrEmpty(chunk.Content) || !string.IsNullOrEmpty(chunk.Thinking))
                    {
                        yield return new ChatResponseChunk
                        {
                            Content = chunk.Content,
                            Thinking = chunk.Thinking
                        };
                    }

                    if (chunk.ToolCalls != null)
                    {
                        toolCalls.AddRange(chunk.ToolCalls);
                    }
                }

                bool hasContent = !string.IsNullOrEmpty(content);
                bool hasToolCalls = toolCalls.Count > 0;
                if (hasContent || hasToolCalls)
                {
                    messagesAppend.Add(new ChatMessage
                    {
                        Role = ChatRole.Assistant,
                        Content = content,
                        ToolCalls = hasToolCalls ? [.. toolCalls] : null
                    });
                }
                else
                {
                    logger.LogWarning("AI returned empty response (no content, no tool calls). Skipping assistant turn to keep history clean.");
                }

                if (hasToolCalls)
                {
                    try
                    {
                        foreach (var toolCall in toolCalls)
                        {
                            yield return new ChatResponseChunk
                            {
                                Content = "",
                                Thinking = "",
                                ToolName = toolCall.FunctionName
                            };

                            var function = toolsProvider.FindFunction(toolCall.FunctionName);
                            if (function != null)
                            {
                                var args = function.BuildArguments(toolCall.Arguments, cancellationToken);
                                var result = function.Invocable(args);
                                var toolResult = await NormalizeToolResultAsync(result);

                                messagesAppend.Add(new ChatMessage
                                {
                                    Role = ChatRole.Tool,
                                    Content = JsonSerializer.Serialize(new
                                    {
                                        status = "success",
                                        content = toolResult.Content,
                                        image_count = toolResult.Images?.Count ?? 0
                                    }),
                                    ToolCallId = toolCall.Id
                                });

                                if (toolResult.Images is { Count: > 0 })
                                {
                                    messagesAppend.Add(new ChatMessage
                                    {
                                        Role = ChatRole.User,
                                        Content = "[도구 결과] 과거 채팅에서 가져온 참조 이미지입니다. 이 이미지를 보고 사용자의 원래 질문에 답하세요.",
                                        Images = toolResult.Images
                                    });
                                }
                            }
                            else
                            {
                                messagesAppend.Add(new ChatMessage
                                {
                                    Role = ChatRole.Tool,
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

            m_Messages.AddRange(messagesAppend.Select(TrimForMemory).Where(ShouldRemember));

            if (m_Messages.Count >= options.MemorySize)
            {
                await SummarizeHistoryAsync(cancellationToken);
            }

            yield break;
        }
        finally
        {
            m_Semaphore.Release();
        }
    }

    private static async Task<ToolExecutionResult> NormalizeToolResultAsync(object? result)
    {
        switch (result)
        {
            case null:
                return ToolExecutionResult.FromText(string.Empty);
            case ToolExecutionResult toolExecutionResult:
                return toolExecutionResult;
            case Task<ToolExecutionResult> toolExecutionResultTask:
                return await toolExecutionResultTask;
            case Task<string> stringTask:
                return ToolExecutionResult.FromText(await stringTask);
            case Task task:
                await task;
                var resultProperty = task.GetType().GetProperty("Result");
                return NormalizeToolResult(resultProperty?.GetValue(task));
            default:
                return NormalizeToolResult(result);
        }
    }

    private static ToolExecutionResult NormalizeToolResult(object? result)
    {
        return result switch
        {
            null => ToolExecutionResult.FromText(string.Empty),
            ToolExecutionResult toolExecutionResult => toolExecutionResult,
            string text => ToolExecutionResult.FromText(text),
            _ => ToolExecutionResult.FromText(result.ToString())
        };
    }

    private static ChatMessage TrimForMemory(ChatMessage message)
    {
        if (message.Images is not { Count: > 0 })
        {
            return message;
        }

        var content = string.IsNullOrWhiteSpace(message.Content)
            ? "[이미지 첨부됨]"
            : message.Content + "\n[이미지 첨부됨]";

        return message with
        {
            Content = content,
            Images = null
        };
    }

    private static bool ShouldRemember(ChatMessage message)
    {
        if (message.Role == ChatRole.Tool)
        {
            return false;
        }

        if (message.ToolCalls is { Count: > 0 })
        {
            return false;
        }

        return message.Role != ChatRole.Assistant || !string.IsNullOrWhiteSpace(message.Content);
    }

    private void PruneRememberedMessages()
    {
        m_Messages.RemoveAll(message => !ShouldRemember(message));
    }

    public async Task TrySummarizeAsync(CancellationToken cancellationToken = default)
    {
        await m_Semaphore.WaitAsync(cancellationToken);
        try
        {
            if (m_Messages.Count >= options.MemorySize)
            {
                logger.LogInformation("Trying summarize chat history.");
                await SummarizeHistoryAsync(cancellationToken);
            }
        }
        finally
        {
            m_Semaphore.Release();
        }
    }

    private async Task SummarizeHistoryAsync(CancellationToken cancellationToken)
    {
        PruneRememberedMessages();

        int summaryRange = m_Messages.Count - 2;
        if (summaryRange <= 0)
        {
            return;
        }

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

        try
        {
            var settings = await claudeSettings.GetAsync(cancellationToken);
            var summaryOptions = new ChatCompletionOptions
            {
                Model = settings.SummaryModel,
                Temperature = 0.5f,
                MaxTokens = settings.DefaultMaxTokens
            };

            var result = await chatClient.GenerateAsync(summaryPrompt, summaryOptions, summarySystem, cancellationToken);
            m_Messages.RemoveRange(0, summaryRange);
            m_Messages.Insert(0, new ChatMessage
            {
                Role = ChatRole.System,
                Content = $"[이전 대화 요약]: {result}"
            });

            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug("The {count} messages were summarized as follows: {summary}", summaryRange, result);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to generate summary message.");
        }
    }
}
