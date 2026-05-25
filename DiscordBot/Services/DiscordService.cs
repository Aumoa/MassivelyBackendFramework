using Discord;
using Discord.Rest;
using Discord.WebSocket;
using DiscordBot.Games.Chess;
using DiscordBot.Repositories;
using DiscordBot.Services.ImageGeneration;
using Microsoft.Extensions.Options;

namespace DiscordBot.Services;

internal class DiscordService(IOptions<DiscordService.Configuration> options, ILogger<DiscordService> logger, OllamaService ollama, IServiceScopeFactory scopeFactory, IHttpClientFactory httpClientFactory, IImageGenerationClient imageGenerationClient, IChessGameService chessGameService) : IHostedService, IAsyncDisposable
{
    public record Configuration
    {
        public required string Token { get; set; }

        public string? AdminBaseUrl { get; set; }
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
                var requestService = scope.ServiceProvider.GetRequiredService<IAllowedChannelRequestService>();
                var guildChannel = message.Channel as SocketGuildChannel;
                var request = await requestService.GetOrCreateAsync(
                    channelId,
                    guildId,
                    guildChannel?.Name,
                    guildChannel?.Guild.Name,
                    message.Author.Id.ToString(),
                    message.Author.Username,
                    message.Id.ToString());
                var approvalUrl = BuildAdminUrl($"/channel-requests/{request.Token}");
                await message.Channel.SendMessageAsync(
                    "허용되지 않은 채널입니다. 관리자에게 아래 승인 링크를 전달해 주세요.\n" +
                    approvalUrl);
            }

            return;
        }

        var processedImages = await ProcessImageAttachmentsAsync(message, scope.ServiceProvider);
        await SaveChatLogAsync(
            message.Id.ToString(),
            guildId,
            channelId,
            message.Author.Id.ToString(),
            content,
            processedImages.Select(image => image.StoredImage).ToList());

        if (!isMentioned)
        {
            return;
        }

        var activeChessGame = chessGameService.FindActiveByUser(message.Author.Id.ToString());
        if (activeChessGame != null && activeChessGame.ChannelId != channelId)
        {
            var notice = await message.Channel.SendMessageAsync("이미 다른 채널에서 체스 게임을 진행 중입니다. 게임을 시작한 채널에서 계속하거나 먼저 종료해 주세요.");
            await SaveChatLogAsync(notice.Id.ToString(), guildId, channelId, m_Socket.CurrentUser.Id.ToString(), notice.Content);
            return;
        }

        var isChessMode = activeChessGame != null;

        string totalReasoning = string.Empty;
        string totalMessage = string.Empty;
        var channel = ollama.GetChannel(message.Channel);
        RestUserMessage? sentMessage = null;
        DateTime? lastEditTime = default;
        bool hasModify = false;

        var chatLogRepository = scope.ServiceProvider.GetRequiredService<IChatLogRepository>();
        var chatImageRepository = scope.ServiceProvider.GetRequiredService<IChatImageRepository>();
        var discordTools = new DiscordTools(m_Socket.CurrentUser, message, chatLogRepository);
        var imageToolsLogger = scope.ServiceProvider.GetRequiredService<ILogger<DiscordImageTools>>();
        var chatImageToolsLogger = scope.ServiceProvider.GetRequiredService<ILogger<DiscordChatImageTools>>();
        var chessToolsLogger = scope.ServiceProvider.GetRequiredService<ILogger<DiscordChessTools>>();
        var promptProfileProvider = scope.ServiceProvider.GetRequiredService<ImagePromptProfileProvider>();
        var chatClient = scope.ServiceProvider.GetRequiredService<AI.IChatClient>();
        var claudeSettings = scope.ServiceProvider.GetRequiredService<IClaudeSettingsService>();
        var imageTools = new DiscordImageTools(message, chatClient, claudeSettings, imageGenerationClient, promptProfileProvider, imageToolsLogger);
        var chatImageTools = new DiscordChatImageTools(message, chatImageRepository, chatImageToolsLogger);
        var chessTools = new DiscordChessTools(m_Socket.CurrentUser, message, chessGameService, chatLogRepository, chessToolsLogger);
        var calculationTools = new AI.Tools.CalculationTools();
        var toolsProvider = isChessMode
            ? AI.ToolsProvider.CreateFrom(chessTools)
            : AI.ToolsProvider.CreateFrom(discordTools, imageTools, chatImageTools, chessTools, calculationTools);
        var toolSettings = scope.ServiceProvider.GetRequiredService<IToolSettingsService>();
        await toolSettings.ApplyAsync(toolsProvider);

        var imageData = processedImages.Count > 0
            ? processedImages.Select(image => image.ChatImage).ToList()
            : null;

        IDisposable? typingState = message.Channel.EnterTypingState();
        string thinkingTicker = "";
        List<string> toolNames = [];
        try
        {
            var prompt = isChessMode
                ? BuildChessModePrompt(chessGameService, message)
                : message.Content;

            await foreach (var responseMessage in channel.AddAsync(message.Author, prompt, toolsProvider, imageData))
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

    private static string BuildChessModePrompt(IChessGameService chessGameService, SocketMessage message)
    {
        var instruction = chessGameService.BuildActiveGameInstruction(
            message.Author.Id.ToString(),
            message.Channel.Id.ToString());

        return $"""
[체스 게임 모드]
현재 사용자는 체스 게임을 진행 중입니다.
사용자의 메시지가 이동/기권/보드 확인이면 반드시 체스 도구를 사용하세요.
사용자가 명백히 일반 대화를 원하면 길게 답하지 말고 "현재 체스 게임 진행 중입니다. 계속 둘까요, 종료할까요?"처럼 게임 진행 여부를 확인하세요.
체스 도구 결과의 상태가 game_over이면 체스 세션은 이미 종료된 것입니다. 종료 요약을 그대로 전달하고 새 게임 권유, 칭찬, 다음 수 안내, 추가 해설을 덧붙이지 마세요.

{instruction}

[사용자 메시지]
{message.Content}
""";
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

    private async Task<List<ProcessedChatImage>> ProcessImageAttachmentsAsync(SocketMessage message, IServiceProvider serviceProvider)
    {
        var imageAttachments = message.Attachments
            .Where(IsImageAttachment)
            .ToList();
        if (imageAttachments.Count == 0)
        {
            return [];
        }

        List<ProcessedChatImage> images = [];
        using var httpClient = httpClientFactory.CreateClient();
        var imageProcessor = serviceProvider.GetRequiredService<IChatLogImageProcessor>();

        foreach (var attachment in imageAttachments)
        {
            try
            {
                var bytes = await httpClient.GetByteArrayAsync(attachment.Url);
                var processedImage = await imageProcessor.ProcessAsync(attachment.Filename, bytes);
                images.Add(processedImage);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to process attachment image: {url}", attachment.Url);
            }
        }

        return images;
    }

    private static bool IsImageAttachment(Attachment attachment)
    {
        if (attachment.ContentType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }

        var extension = Path.GetExtension(attachment.Filename);
        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".gif", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".webp", StringComparison.OrdinalIgnoreCase);
    }

    private string BuildAdminUrl(string path)
    {
        var normalizedPath = path.StartsWith('/') ? path : "/" + path;
        var baseUrl = options.Value.AdminBaseUrl?.Trim().TrimEnd('/');
        if (Uri.TryCreate(baseUrl, UriKind.Absolute, out _))
        {
            return baseUrl + normalizedPath;
        }

        return normalizedPath;
    }

    private Task OnLog(LogMessage message)
    {
        if (logger.IsEnabled(LogLevel.Trace))
        {
            logger.LogTrace("{log}", message.ToString());
        }
        return Task.CompletedTask;
    }

    private async Task SaveChatLogAsync(
        string? messageId,
        string? guildId,
        string channelId,
        string userId,
        string content,
        IReadOnlyList<ChatLogImageInput>? images = null)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IChatLogRepository>();
            await repository.AddAsync(messageId, guildId, channelId, userId, content, images);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to save chat log");
        }
    }
}
