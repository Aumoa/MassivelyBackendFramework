using System.Runtime.CompilerServices;
using System.Text.Json;
using AI;
using Discord;

namespace DiscordBot.Services;

public class OllamaChatHistory(ILogger logger, OllamaService.Configuration options, IChatClient chatClient)
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
                var allMessages = recentHistory.Concat(messagesAppend).ToList();
                var chatOptions = new ChatCompletionOptions
                {
                    Model = options.Model,
                    Temperature = 0.7f,
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
                                string toolResult;

                                if (result is Task<string> taskResult)
                                    toolResult = await taskResult;
                                else
                                    toolResult = result?.ToString() ?? string.Empty;

                                messagesAppend.Add(new ChatMessage
                                {
                                    Role = ChatRole.Tool,
                                    Content = JsonSerializer.Serialize(new { status = "success", content = toolResult }),
                                    ToolCallId = toolCall.Id
                                });
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

            m_Messages.AddRange(messagesAppend);

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

        try
        {
            var summaryOptions = new ChatCompletionOptions
            {
                Model = options.SummaryModel,
                Temperature = 0.5f
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
