using Discord;
using Discord.Rest;
using Discord.WebSocket;
using DiscordBot.Repositories;
using DiscordBot.Services.ImageGeneration;
using Microsoft.Extensions.Options;

namespace DiscordBot.Services;

public class DiscordService(IOptions<DiscordService.Configuration> options, ILogger<DiscordService> logger, OllamaService ollama, IServiceScopeFactory scopeFactory, IHttpClientFactory httpClientFactory, IImageGenerationClient imageGenerationClient) : IHostedService, IAsyncDisposable
{
    public record Configuration
    {
        public required string Token { get; set; }
    }

    private readonly DiscordSocketClient m_Socket = new(new DiscordSocketConfig
    {
        GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent
    });

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        m_Socket.MessageReceived += OnMessageReceived;
        m_Socket.Log += OnLog;

        await m_Socket.LoginAsync(TokenType.Bot, options.Value.Token);
        await m_Socket.StartAsync();
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        m_Socket.MessageReceived -= OnMessageReceived;
        m_Socket.Log -= OnLog;

        await m_Socket.StopAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await m_Socket.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    private static readonly Emoji s_Emoji = "✍️";

    private async Task OnMessageReceived(SocketMessage message)
    {
        var content = message.Content;

        if (message.Author.IsBot)
        {
            return;
        }

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("Message: {message}", content.ToString());
        }

        bool isMentioned = message.MentionedUsers.Any(u => u.Id == m_Socket.CurrentUser.Id);
        var guildId = (message.Channel as SocketGuildChannel)?.Guild.Id.ToString();
        var channelId = message.Channel.Id.ToString();

        using var scope = scopeFactory.CreateScope();
        var channelAccess = scope.ServiceProvider.GetRequiredService<IAllowedChannelService>();
        if (!await channelAccess.IsAllowedAsync(channelId))
        {
            if (isMentioned)
            {
                await message.Channel.SendMessageAsync($"허용되지 않은 채널입니다. 관리자에게 요청하세요. 채널 ID: {channelId}");
            }

            return;
        }

        await SaveChatLogAsync(message.Id.ToString(), guildId, channelId, message.Author.Id.ToString(), content);

        if (!isMentioned)
        {
            return;
        }

        string totalReasoning = string.Empty;
        string totalMessage = string.Empty;
        string cleanPrompt = message.Content.Replace($"<@{m_Socket.CurrentUser.Id}>", "").Trim();
        var channel = ollama.GetChannel(message.Channel);
        RestUserMessage? sentMessage = null;
        DateTime? lastEditTime = default;
        bool hasModify = false;
        List<Task> emojiTasks = [];

        var chatLogRepository = scope.ServiceProvider.GetRequiredService<IChatLogRepository>();
        var discordTools = new DiscordTools(m_Socket.CurrentUser, message, chatLogRepository);
        var imageToolsLogger = scope.ServiceProvider.GetRequiredService<ILogger<DiscordImageTools>>();
        var promptProfileProvider = scope.ServiceProvider.GetRequiredService<ImagePromptProfileProvider>();
        var imageTools = new DiscordImageTools(message, imageGenerationClient, promptProfileProvider, imageToolsLogger);
        var calculationTools = new AI.Tools.CalculationTools();
        var toolsProvider = AI.ToolsProvider.CreateFrom(discordTools, imageTools, calculationTools);

        List<AI.ChatImage>? imageData = null;
        var imageAttachments = message.Attachments
            .Where(a => a.ContentType?.StartsWith("image/") == true)
            .ToList();

        if (imageAttachments.Count > 0)
        {
            imageData = [];
            using var httpClient = httpClientFactory.CreateClient();
            foreach (var attachment in imageAttachments)
            {
                try
                {
                    var bytes = await httpClient.GetByteArrayAsync(attachment.Url);
                    imageData.Add(new AI.ChatImage
                    {
                        Base64 = Convert.ToBase64String(bytes),
                        MediaType = attachment.ContentType
                    });
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Failed to download attachment: {url}", attachment.Url);
                }
            }
        }

        IDisposable? typingState = message.Channel.EnterTypingState();
        string thinkingTicker = "";
        List<string> toolNames = [];
        try
        {
            await foreach (var responseMessage in channel.AddAsync(message.Author, message.Content, toolsProvider, imageData))
            {
                totalReasoning += responseMessage.Thinking;
                totalMessage += responseMessage.Content;
                hasModify = true;

                if (!string.IsNullOrEmpty(responseMessage.ToolName))
                {
                    toolNames.Add(responseMessage.ToolName);
                }

                if (logger.IsEnabled(LogLevel.Debug))
                {
                    logger.LogDebug("Message Chunk Received: {chunk}", responseMessage);
                }

                if (lastEditTime.HasValue == false)
                {
                    lastEditTime = DateTime.UtcNow;
                }

                var span = DateTime.UtcNow - lastEditTime.Value;
                if (span.TotalSeconds <= 1)
                {
                    continue;
                }

                string currentMessage;
                if (string.IsNullOrEmpty(totalMessage))
                {
                    currentMessage = $"*Thinking{thinkingTicker}*";
                    thinkingTicker += "\\*";

                    if (toolNames.Count > 0)
                    {
                        currentMessage += "\n" + string.Join("\n", toolNames.Select(t => $"🔧 *{t}*"));
                    }
                }
                else
                {
                    currentMessage = totalMessage;
                }

                lastEditTime = DateTime.UtcNow;
                if (sentMessage == null)
                {
                    sentMessage = await message.Channel.SendMessageAsync(currentMessage);
                    typingState?.Dispose();
                    typingState = null;
                    await sentMessage.AddReactionAsync(s_Emoji);
                }
                else
                {
                    await sentMessage.ModifyAsync(p => p.Content = currentMessage);
                    hasModify = false;
                }
            }

            if (sentMessage == null)
            {
                if (!string.IsNullOrEmpty(totalMessage))
                {
                    sentMessage = await message.Channel.SendMessageAsync(totalMessage);
                    typingState?.Dispose();
                    typingState = null;
                }
            }
            else if (hasModify)
            {
                await sentMessage.ModifyAsync(p => p.Content = totalMessage);
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to generate AI response.");
            totalMessage = BuildAIErrorMessage(e);

            if (sentMessage == null)
            {
                sentMessage = await message.Channel.SendMessageAsync(totalMessage);
                typingState?.Dispose();
                typingState = null;
            }
            else
            {
                await sentMessage.ModifyAsync(p => p.Content = totalMessage);
            }
        }
        finally
        {
            typingState?.Dispose();
            typingState = null;

            if (!string.IsNullOrEmpty(totalMessage))
            {
                await SaveChatLogAsync(sentMessage?.Id.ToString(), guildId, message.Channel.Id.ToString(), m_Socket.CurrentUser.Id.ToString(), totalMessage);
            }

            if (sentMessage != null)
            {
                try
                {
                    await sentMessage.RemoveReactionAsync(s_Emoji, m_Socket.CurrentUser);
                }
                catch (Exception e)
                {
                    logger.LogError("Failed to remove reaction: {e}", e);
                }
            }
        }

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("Response: {message}", totalMessage);
        }

        await channel.TrySummarizeAsync();
    }

    private static string BuildAIErrorMessage(Exception exception)
    {
        var message = exception.Message;
        if (message.Contains("overloaded", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("529", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("capacity", StringComparison.OrdinalIgnoreCase))
        {
            return "현재 AI API가 혼잡합니다. 잠시 후 다시 시도해 주세요.";
        }

        return "응답 생성 중 오류가 발생했습니다. 잠시 후 다시 시도해 주세요.";
    }

    private Task OnLog(LogMessage message)
    {
        if (logger.IsEnabled(LogLevel.Trace))
        {
            logger.LogTrace("{log}", message.ToString());
        }
        return Task.CompletedTask;
    }

    private async Task SaveChatLogAsync(string? messageId, string? guildId, string channelId, string userId, string content)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IChatLogRepository>();
            await repository.AddAsync(messageId, guildId, channelId, userId, content);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to save chat log");
        }
    }
}
