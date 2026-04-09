using Discord;
using Discord.Rest;
using Discord.WebSocket;
using DiscordBot.Repositories;
using Microsoft.Extensions.Options;

namespace DiscordBot.Services;

public class DiscordService(IOptions<DiscordService.Configuration> options, ILogger<DiscordService> logger, OllamaService ollama, IServiceScopeFactory scopeFactory) : IHostedService, IAsyncDisposable
{
    public record Configuration
    {
        public required string Token { get; set; }
    }

    private readonly DiscordSocketClient m_Socket = new();

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

    private class ToolsProvider(SocketSelfUser selfUser, SocketMessage message) : IToolsProvider
    {
        public async Task<string> GetMessagesAsync(int limit, CancellationToken cancellationToken = default)
        {
            List<string> totalMessages = [];

            await foreach (var dmc in message.Channel.GetMessagesAsync(limit, CacheMode.AllowDownload))
            {
                foreach (var dm in dmc)
                {
                    if (dm.Author.Id == selfUser.Id)
                    {
                        totalMessages.Add($"[{dm.CreatedAt}](나의 응답): {dm.Content}");
                    }
                    else
                    {
                        totalMessages.Add($"[{dm.CreatedAt}]({dm.Author.Username}님의 메시지): {dm.Content}");
                    }
                }
            }

            return string.Join("\n", totalMessages);
        }

        public Task<string> GetCurrentDateAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(DateTimeOffset.UtcNow.ToString());
        }
    }

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
        if (!isMentioned)
        {
            return;
        }

        await SaveChatLogAsync(message.Channel.Id.ToString(), message.Author.Id.ToString(), content);

        string totalReasoning = string.Empty;
        string totalMessage = string.Empty;
        string cleanPrompt = message.Content.Replace($"<@{m_Socket.CurrentUser.Id}>", "").Trim();
        var channel = ollama.GetChannel(message.Channel);
        RestUserMessage? sentMessage = null;
        DateTime? lastEditTime = default;
        bool hasModify = false;
        List<Task> emojiTasks = [];
        var toolsProvider = new ToolsProvider(m_Socket.CurrentUser, message);

        IDisposable? typingState = message.Channel.EnterTypingState();
        string thinkingTicker = "";
        try
        {
            await foreach (var responseMessage in channel.AddAsync(message.Author, message.Content, toolsProvider))
            {
                totalReasoning += responseMessage.Thinking;
                totalMessage += responseMessage.Content;
                hasModify = true;

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

            if (sentMessage != null)
            {
                if (hasModify)
                {
                    await sentMessage.ModifyAsync(p => p.Content = totalMessage);
                }
            }
        }
        finally
        {
            typingState?.Dispose();
            typingState = null;

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

        if (!string.IsNullOrEmpty(totalMessage))
        {
            await SaveChatLogAsync(message.Channel.Id.ToString(), m_Socket.CurrentUser.Id.ToString(), totalMessage);
        }

        await channel.TrySummarizeAsync();
    }

    private Task OnLog(LogMessage message)
    {
        if (logger.IsEnabled(LogLevel.Trace))
        {
            logger.LogTrace("{log}", message.ToString());
        }
        return Task.CompletedTask;
    }

    private async Task SaveChatLogAsync(string channelId, string userId, string content)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IChatLogRepository>();
            await repository.AddAsync(channelId, userId, content);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to save chat log");
        }
    }
}
