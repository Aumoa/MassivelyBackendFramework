using Discord;
using Discord.Rest;
using Discord.WebSocket;
using DiscordBot.Games.Chess;
using DiscordBot.Games.Othello;
using DiscordBot.Options;
using DiscordBot.Repositories;
using DiscordBot.Services.ImageGeneration;
using Microsoft.Extensions.Options;

namespace DiscordBot.Services;

internal class DiscordService(IOptions<DiscordService.Configuration> options, ILogger<DiscordService> logger, OllamaService ollama, IServiceScopeFactory scopeFactory, IDiscordAttachmentDownloader attachmentDownloader, IImageGenerationClient imageGenerationClient, IChessGameService chessGameService, IOthelloGameService othelloGameService, IDiscordAutoResponseCoordinator autoResponse) : IHostedService, IAsyncDisposable
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
    private static readonly TimeSpan s_ResponseEditInterval = TimeSpan.FromSeconds(5);
    private const int DiscordSafeMessageLength = 1900;
    private const int MaxReferencedImageCount = 4;

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
        bool isDirectMessage = message.Channel is IDMChannel;
        bool shouldRespond = ShouldRespondToMessage(isMentioned, isDirectMessage);
        var guildId = (message.Channel as SocketGuildChannel)?.Guild.Id.ToString();
        var channelId = message.Channel.Id.ToString();

        using var scope = scopeFactory.CreateScope();
        var processedImages = await ProcessImageAttachmentsAsync(message, scope.ServiceProvider);
        var processedAttachments = await ProcessDocumentAttachmentsAsync(message, scope.ServiceProvider);
        var referencedMessageId = GetReferencedMessageId(message);
        var referencedChannelId = GetReferencedChannelId(message);
        var referencedGuildId = GetReferencedGuildId(message);
        await SaveChatLogAsync(
            message.Id.ToString(),
            guildId,
            channelId,
            message.Author.Id.ToString(),
            content,
            processedImages.Select(image => image.StoredImage).ToList(),
            processedAttachments.Select(attachment => attachment.StoredAttachment).ToList(),
            referencedMessageId,
            referencedChannelId,
            referencedGuildId);

        if (!shouldRespond)
        {
            if (!isDirectMessage)
            {
                await QueueAutoResponseAsync(
                    message,
                    guildId,
                    channelId,
                    isMentioned,
                    processedImages,
                    processedAttachments,
                    scope.ServiceProvider);
            }

            return;
        }

        autoResponse.CancelPending(channelId);
        await GenerateResponseAsync(
            message,
            guildId,
            channelId,
            processedImages,
            processedAttachments);
    }

    private async Task GenerateResponseAsync(
        SocketMessage message,
        string? guildId,
        string channelId,
        IReadOnlyList<ProcessedChatImage> processedImages,
        IReadOnlyList<ProcessedChatAttachment> processedAttachments,
        DiscordAutoResponseRequest? autoResponseRequest = null,
        CancellationToken cancellationToken = default)
    {
        var isAutomaticResponse = autoResponseRequest != null;
        var activeChessGame = chessGameService.FindActiveByUser(message.Author.Id.ToString());
        var activeOthelloGame = othelloGameService.FindActiveByUser(message.Author.Id.ToString());
        var activeGameName = activeChessGame != null ? "체스" : activeOthelloGame != null ? "오셀로" : null;
        var activeGameChannelId = activeChessGame?.ChannelId ?? activeOthelloGame?.ChannelId;
        if (isAutomaticResponse && activeGameChannelId != null)
        {
            logger.LogDebug(
                "Skipping automatic response for user {UserId}: active game is in progress.",
                message.Author.Id);
            return;
        }

        if (activeGameChannelId != null && activeGameChannelId != channelId)
        {
            var notice = await message.Channel.SendMessageAsync($"이미 다른 채널에서 {activeGameName} 게임을 진행 중입니다. 게임을 시작한 채널에서 계속하거나 먼저 종료해 주세요.");
            await SaveChatLogAsync(notice.Id.ToString(), guildId, channelId, m_Socket.CurrentUser.Id.ToString(), notice.Content);
            return;
        }

        var isChessMode = activeChessGame != null;
        var isOthelloMode = activeOthelloGame != null;

        string totalReasoning = string.Empty;
        string totalMessage = string.Empty;
        var channel = ollama.GetChannel(message.Channel);
        RestUserMessage? sentMessage = null;
        DateTime? lastEditTime = default;

        using var scope = scopeFactory.CreateScope();
        var chatLogRepository = scope.ServiceProvider.GetRequiredService<IChatLogRepository>();
        var chatImageRepository = scope.ServiceProvider.GetRequiredService<IChatImageRepository>();
        var chatAttachmentRepository = scope.ServiceProvider.GetRequiredService<IChatAttachmentRepository>();
        var appointmentRepository = scope.ServiceProvider.GetRequiredService<IAppointmentRepository>();
        var channelNoteRepository = scope.ServiceProvider.GetRequiredService<IChannelNoteRepository>();
        var discordTools = new DiscordTools(m_Socket.CurrentUser, message, chatLogRepository, appointmentRepository, channelNoteRepository);
        var imageToolsLogger = scope.ServiceProvider.GetRequiredService<ILogger<DiscordImageTools>>();
        var chatImageToolsLogger = scope.ServiceProvider.GetRequiredService<ILogger<DiscordChatImageTools>>();
        var chatAttachmentToolsLogger = scope.ServiceProvider.GetRequiredService<ILogger<DiscordChatAttachmentTools>>();
        var chessToolsLogger = scope.ServiceProvider.GetRequiredService<ILogger<DiscordChessTools>>();
        var othelloToolsLogger = scope.ServiceProvider.GetRequiredService<ILogger<DiscordOthelloTools>>();
        var aiConfigurationToolsLogger = scope.ServiceProvider.GetRequiredService<ILogger<DiscordAiConfigurationTools>>();
        var userAuthorizationToolsLogger = scope.ServiceProvider.GetRequiredService<ILogger<DiscordUserAuthorizationTools>>();
        var webPageToolsLogger = scope.ServiceProvider.GetRequiredService<ILogger<DiscordWebPageTools>>();
        var promptProfileProvider = scope.ServiceProvider.GetRequiredService<ImagePromptProfileProvider>();
        var httpClientFactory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();
        var webPageAddressResolver = scope.ServiceProvider.GetRequiredService<IWebPageAddressResolver>();
        var webPageReadOptions = scope.ServiceProvider.GetRequiredService<IOptions<WebPageReadOptions>>();
        var chatClient = scope.ServiceProvider.GetRequiredService<AI.IChatClient>();
        var claudeSettings = scope.ServiceProvider.GetRequiredService<IClaudeSettingsService>();
        var aiSkillManagement = scope.ServiceProvider.GetRequiredService<IAiSkillManagementService>();
        var aiConfigurationOptions = scope.ServiceProvider.GetRequiredService<IOptions<AiConfigurationManagementOptions>>();
        var userPermissions = scope.ServiceProvider.GetRequiredService<IDiscordUserPermissionService>();
        var imageTools = new DiscordImageTools(message, chatClient, claudeSettings, imageGenerationClient, promptProfileProvider, imageToolsLogger);
        var chatImageTools = new DiscordChatImageTools(message, chatImageRepository, chatImageToolsLogger);
        var chatAttachmentTools = new DiscordChatAttachmentTools(message, chatAttachmentRepository, chatAttachmentToolsLogger);
        var chessTools = new DiscordChessTools(m_Socket.CurrentUser, message, chessGameService, othelloGameService, chatLogRepository, chessToolsLogger);
        var othelloTools = new DiscordOthelloTools(m_Socket.CurrentUser, message, othelloGameService, chessGameService, chatLogRepository, othelloToolsLogger);
        var aiConfigurationTools = new DiscordAiConfigurationTools(message, claudeSettings, aiSkillManagement, aiConfigurationOptions, aiConfigurationToolsLogger);
        var userAuthorizationTools = new DiscordUserAuthorizationTools(m_Socket, message, userPermissions, userAuthorizationToolsLogger);
        var webPageTools = new DiscordWebPageTools(httpClientFactory, webPageAddressResolver, webPageReadOptions, webPageToolsLogger);
        var calculationTools = new AI.Tools.CalculationTools();
        var toolsProvider = isChessMode
            ? AI.ToolsProvider.CreateFrom(chessTools)
            : isOthelloMode
                ? AI.ToolsProvider.CreateFrom(othelloTools)
                : AI.ToolsProvider.CreateFrom(discordTools, imageTools, chatImageTools, chatAttachmentTools, chessTools, othelloTools, aiConfigurationTools, userAuthorizationTools, webPageTools, calculationTools);
        var toolSettings = scope.ServiceProvider.GetRequiredService<IToolSettingsService>();
        await toolSettings.ApplyAsync(toolsProvider);

        var currentMessageImages = processedImages
            .Select(image => image.ChatImage)
            .ToList();

        IDisposable? typingState = message.Channel.EnterTypingState();
        string thinkingTicker = "";
        var pendingToolUseCount = 0;
        bool shouldSeparateNextAssistantContent = false;
        try
        {
            var promptContent = isAutomaticResponse
                ? DiscordAutoResponseEvaluator.BuildAutomaticResponsePrompt(
                    autoResponseRequest!.Messages,
                    autoResponseRequest.Decision)
                : DiscordMessageAttachmentPlanner.BuildPromptContent(message.Content, processedAttachments);
            var referencedChatLog = await GetReferencedChatLogAsync(
                message,
                chatLogRepository,
                channelId);
            var referencedImages = await GetReferencedChatImagesAsync(
                message,
                chatImageRepository,
                channelId);
            var imageData = BuildPromptImages(currentMessageImages, referencedImages);
            promptContent = BuildPromptContentWithReferencedMessage(
                promptContent,
                referencedChatLog,
                m_Socket.CurrentUser.Id.ToString(),
                referencedImages.Count);

            var prompt = isChessMode
                ? BuildChessModePrompt(chessGameService, message, promptContent)
                : isOthelloMode
                    ? BuildOthelloModePrompt(othelloGameService, message, promptContent)
                    : promptContent;

            await foreach (var responseMessage in channel.AddAsync(
                message.Author,
                prompt,
                toolsProvider,
                imageData,
                filterToolsBySelectedSkills: !isChessMode && !isOthelloMode,
                rememberConversation: !isAutomaticResponse,
                cancellationToken: cancellationToken))
            {
                totalReasoning += responseMessage.Thinking;
                if (!isAutomaticResponse && responseMessage.SkillNames.Count > 0)
                {
                    totalMessage = AppendSkillUseNotice(
                        totalMessage,
                        responseMessage.SkillNames,
                        ref shouldSeparateNextAssistantContent);
                }

                if (!isAutomaticResponse && !string.IsNullOrEmpty(responseMessage.Content) && pendingToolUseCount > 0)
                {
                    totalMessage = AppendToolUseNotice(
                        totalMessage,
                        pendingToolUseCount,
                        ref shouldSeparateNextAssistantContent);
                    pendingToolUseCount = 0;
                }

                totalMessage = AppendResponseContent(
                    totalMessage,
                    responseMessage.Content,
                    ref shouldSeparateNextAssistantContent);

                if (!string.IsNullOrEmpty(responseMessage.ToolName))
                {
                    if (!isAutomaticResponse)
                    {
                        pendingToolUseCount++;
                        shouldSeparateNextAssistantContent = !string.IsNullOrWhiteSpace(totalMessage);
                    }
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
                if (span < s_ResponseEditInterval)
                {
                    continue;
                }

                if (isAutomaticResponse && string.IsNullOrEmpty(totalMessage))
                {
                    continue;
                }

                string currentMessage;
                if (string.IsNullOrEmpty(totalMessage))
                {
                    currentMessage = $"*Thinking{thinkingTicker}*";
                    thinkingTicker += "\\*";

                    if (pendingToolUseCount > 0)
                    {
                        currentMessage += "\n" + BuildToolUseNotice(pendingToolUseCount);
                    }
                }
                else
                {
                    currentMessage = BuildDiscordPreview(BuildResponsePreview(totalMessage, pendingToolUseCount));
                }

                currentMessage = BuildDiscordPreview(currentMessage);
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
                }
            }

            if (sentMessage == null)
            {
                if (!string.IsNullOrEmpty(totalMessage))
                {
                    sentMessage = await SendDiscordResponseAsync(message.Channel, totalMessage);
                    typingState?.Dispose();
                    typingState = null;
                }
            }
            else if (!string.IsNullOrEmpty(totalMessage))
            {
                await FinalizeDiscordResponseAsync(message.Channel, sentMessage, totalMessage);
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
                await sentMessage.ModifyAsync(p => p.Content = BuildDiscordPreview(totalMessage));
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

        if (!isAutomaticResponse)
        {
            await channel.TrySummarizeAsync();
        }
    }

    private async Task QueueAutoResponseAsync(
        SocketMessage message,
        string? guildId,
        string channelId,
        bool isMentioned,
        IReadOnlyList<ProcessedChatImage> processedImages,
        IReadOnlyList<ProcessedChatAttachment> processedAttachments,
        IServiceProvider serviceProvider)
    {
        var referencesBotMessage = await ReferencesBotMessageAsync(
            message,
            channelId,
            serviceProvider);
        var autoResponseMessage = new DiscordAutoResponseMessage(
            message.Id.ToString(),
            guildId,
            channelId,
            message.Author.Id.ToString(),
            message.Author.Username,
            message.Content,
            message.Timestamp,
            IsDirectMessage: false,
            MentionsBot: isMentioned,
            ReferencesBotMessage: referencesBotMessage,
            HasAttachments: message.Attachments.Count > 0 || message.Embeds.Count > 0);

        await autoResponse.ObserveAsync(
            autoResponseMessage,
            async (request, cancellationToken) => await GenerateResponseAsync(
                message,
                guildId,
                channelId,
                processedImages,
                processedAttachments,
                request,
                cancellationToken));
    }

    private async ValueTask<bool> ReferencesBotMessageAsync(
        SocketMessage message,
        string channelId,
        IServiceProvider serviceProvider)
    {
        if (GetReferencedMessageId(message) == null)
        {
            return false;
        }

        var chatLogRepository = serviceProvider.GetRequiredService<IChatLogRepository>();
        var referencedChatLog = await GetReferencedChatLogAsync(
            message,
            chatLogRepository,
            channelId);
        return referencedChatLog?.UserId == m_Socket.CurrentUser.Id.ToString();
    }

    private static string BuildDiscordPreview(string content)
    {
        if (content.Length <= DiscordSafeMessageLength)
        {
            return content;
        }

        const string suffix = "\n\n...(응답이 길어 이어서 생성 중입니다.)";
        return content[..(DiscordSafeMessageLength - suffix.Length)].TrimEnd() + suffix;
    }

    internal static string AppendResponseContent(
        string currentMessage,
        string content,
        ref bool shouldSeparateBeforeContent)
    {
        if (string.IsNullOrEmpty(content))
        {
            return currentMessage;
        }

        if (shouldSeparateBeforeContent && !string.IsNullOrWhiteSpace(currentMessage))
        {
            shouldSeparateBeforeContent = false;
            return currentMessage.TrimEnd('\r', '\n') + "\n\n" + content.TrimStart('\r', '\n');
        }

        shouldSeparateBeforeContent = false;
        return currentMessage + content;
    }

    internal static string AppendToolUseNotice(
        string currentMessage,
        int toolUseCount,
        ref bool shouldSeparateBeforeContent)
    {
        if (toolUseCount <= 0)
        {
            return currentMessage;
        }

        shouldSeparateBeforeContent = true;
        var notice = BuildToolUseNotice(toolUseCount);
        if (string.IsNullOrWhiteSpace(currentMessage))
        {
            return notice;
        }

        return currentMessage.TrimEnd('\r', '\n') + "\n\n" + notice;
    }

    internal static string AppendSkillUseNotice(
        string currentMessage,
        IReadOnlyList<string> skillNames,
        ref bool shouldSeparateBeforeContent)
    {
        var notice = BuildSkillUseNotice(skillNames);
        if (string.IsNullOrEmpty(notice))
        {
            return currentMessage;
        }

        shouldSeparateBeforeContent = true;
        if (string.IsNullOrWhiteSpace(currentMessage))
        {
            return notice;
        }

        return currentMessage.TrimEnd('\r', '\n') + "\n\n" + notice;
    }

    internal static string BuildToolUseNotice(int toolUseCount)
    {
        return $"{toolUseCount}개 도구 사용됨";
    }

    internal static string BuildSkillUseNotice(IReadOnlyList<string> skillNames)
    {
        var normalizedSkillNames = skillNames
            .Where(skillName => !string.IsNullOrWhiteSpace(skillName))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (normalizedSkillNames.Length == 0)
        {
            return string.Empty;
        }

        return $"사용된 Skill: {string.Join(", ", normalizedSkillNames)}";
    }

    internal static bool ShouldRespondToMessage(bool isMentioned, bool isDirectMessage)
    {
        return isMentioned || isDirectMessage;
    }

    private static string BuildResponsePreview(string totalMessage, int pendingToolUseCount)
    {
        if (pendingToolUseCount <= 0)
        {
            return totalMessage;
        }

        if (string.IsNullOrWhiteSpace(totalMessage))
        {
            return BuildToolUseNotice(pendingToolUseCount);
        }

        return totalMessage.TrimEnd('\r', '\n') + "\n\n" + BuildToolUseNotice(pendingToolUseCount);
    }

    internal static string BuildPromptContentWithReferencedMessage(
        string promptContent,
        ChatLogData? referencedChatLog,
        string selfUserId,
        int referencedImageCount = 0)
    {
        if (referencedChatLog == null)
        {
            return promptContent;
        }

        var author = referencedChatLog.UserId == selfUserId
            ? "봇의 이전 응답"
            : $"사용자 {referencedChatLog.UserId}의 메시지";
        var imageNotice = referencedImageCount > 0
            ? $"첨부 이미지: {referencedImageCount}장이 현재 사용자 입력 이미지로 함께 포함되었습니다.\n"
            : string.Empty;

        return $"""
[사용자가 답장으로 참조한 메시지]
작성자: {author}
MessageId: {referencedChatLog.MessageId ?? "(unknown)"}
{imageNotice}내용:
{referencedChatLog.Content}

[사용자 메시지]
{promptContent}
""";
    }

    internal static IReadOnlyList<AI.ChatImage>? BuildPromptImages(
        IReadOnlyList<AI.ChatImage> currentMessageImages,
        IReadOnlyList<ChatImageData> referencedImages)
    {
        if (currentMessageImages.Count == 0 && referencedImages.Count == 0)
        {
            return null;
        }

        List<AI.ChatImage> images = [.. currentMessageImages];
        images.AddRange(referencedImages.Select(static image => new AI.ChatImage
        {
            Base64 = Convert.ToBase64String(image.Data),
            MediaType = image.ContentType
        }));

        return images;
    }

    private static async Task<RestUserMessage?> SendDiscordResponseAsync(ISocketMessageChannel channel, string content)
    {
        var chunks = SplitDiscordMessage(content);
        RestUserMessage? firstMessage = null;
        foreach (var chunk in chunks)
        {
            var sent = await channel.SendMessageAsync(chunk);
            firstMessage ??= sent;
        }

        return firstMessage;
    }

    private static async Task FinalizeDiscordResponseAsync(
        ISocketMessageChannel channel,
        RestUserMessage sentMessage,
        string content)
    {
        var chunks = SplitDiscordMessage(content);
        if (chunks.Count == 0)
        {
            return;
        }

        await sentMessage.ModifyAsync(p => p.Content = chunks[0]);
        foreach (var chunk in chunks.Skip(1))
        {
            await channel.SendMessageAsync(chunk);
        }
    }

    private static IReadOnlyList<string> SplitDiscordMessage(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return [];
        }

        List<string> chunks = [];
        var remaining = content.Replace("\r\n", "\n");
        while (remaining.Length > DiscordSafeMessageLength)
        {
            var splitAt = remaining.LastIndexOf('\n', DiscordSafeMessageLength);
            if (splitAt < DiscordSafeMessageLength / 2)
            {
                splitAt = remaining.LastIndexOf(' ', DiscordSafeMessageLength);
            }

            if (splitAt < DiscordSafeMessageLength / 2)
            {
                splitAt = DiscordSafeMessageLength;
            }

            chunks.Add(remaining[..splitAt].TrimEnd());
            remaining = remaining[splitAt..].TrimStart('\n', ' ');
        }

        if (!string.IsNullOrWhiteSpace(remaining))
        {
            chunks.Add(remaining);
        }

        return chunks;
    }

    private static string BuildChessModePrompt(
        IChessGameService chessGameService,
        SocketMessage message,
        string promptContent)
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
{promptContent}
""";
    }

    private static string BuildOthelloModePrompt(
        IOthelloGameService othelloGameService,
        SocketMessage message,
        string promptContent)
    {
        var instruction = othelloGameService.BuildActiveGameInstruction(
            message.Author.Id.ToString(),
            message.Channel.Id.ToString());

        return $"""
[오셀로 게임 모드]
현재 사용자는 오셀로 게임을 진행 중입니다.
사용자의 메시지가 착수/패스/기권/보드 확인이면 반드시 오셀로 도구를 사용하세요.
사용자가 명백히 일반 대화를 원하면 길게 답하지 말고 "현재 오셀로 게임 진행 중입니다. 계속 둘까요, 종료할까요?"처럼 게임 진행 여부를 확인하세요.
오셀로 도구 결과의 상태가 game_over이면 오셀로 세션은 이미 종료된 것입니다. 종료 요약을 그대로 전달하고 새 게임 권유, 칭찬, 다음 수 안내, 추가 해설을 덧붙이지 마세요.

{instruction}

[사용자 메시지]
{promptContent}
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
            .Where(attachment => DiscordMessageAttachmentPlanner.IsImageAttachment(
                attachment.Filename,
                attachment.ContentType))
            .ToList();
        if (imageAttachments.Count == 0)
        {
            return [];
        }

        var downloadOptions = serviceProvider.GetRequiredService<IOptions<AttachmentDownloadOptions>>().Value;
        var imageProcessor = serviceProvider.GetRequiredService<IChatLogImageProcessor>();
        return await DiscordAttachmentBatchProcessor.ProcessAsync(
            imageAttachments,
            async attachment =>
            {
                var bytes = await attachmentDownloader.DownloadAsync(
                    attachment.Url,
                    downloadOptions.MaxImageBytes);
                return await imageProcessor.ProcessAsync(attachment.Filename, bytes);
            },
            (attachment, exception) => logger.LogError(
                exception,
                "Failed to process attachment image: {url}",
                attachment.Url));
    }

    private async Task<List<ProcessedChatAttachment>> ProcessDocumentAttachmentsAsync(
        SocketMessage message,
        IServiceProvider serviceProvider)
    {
        var attachmentProcessor = serviceProvider.GetRequiredService<IChatLogAttachmentProcessor>();
        var documentAttachments = message.Attachments
            .Where(attachment => DiscordMessageAttachmentPlanner.IsDocumentAttachment(
                attachment.Filename,
                attachment.ContentType,
                attachment.Size,
                attachmentProcessor))
            .ToList();
        if (documentAttachments.Count == 0)
        {
            return [];
        }

        var processingOptions = serviceProvider.GetRequiredService<IOptions<AttachmentProcessingOptions>>().Value;
        return await DiscordAttachmentBatchProcessor.ProcessAsync(
            documentAttachments,
            async attachment =>
            {
                var bytes = await attachmentDownloader.DownloadAsync(
                    attachment.Url,
                    processingOptions.MaxAttachmentBytes);
                return await attachmentProcessor.ProcessAsync(
                    attachment.Id.ToString(),
                    attachment.Filename,
                    attachment.ContentType,
                    bytes.LongLength,
                    bytes);
            },
            (attachment, exception) => logger.LogError(
                exception,
                "Failed to process attachment document: {url}",
                attachment.Url));
    }

    private static async ValueTask<ChatLogData?> GetReferencedChatLogAsync(
        SocketMessage message,
        IChatLogRepository chatLogRepository,
        string currentChannelId)
    {
        var referencedMessageId = GetReferencedMessageId(message);
        if (string.IsNullOrWhiteSpace(referencedMessageId))
        {
            return null;
        }

        var referencedChannelId = GetReferencedChannelId(message) ?? currentChannelId;
        if (!string.Equals(referencedChannelId, currentChannelId, StringComparison.Ordinal))
        {
            return null;
        }

        return await chatLogRepository.GetByMessageIdAsync(
            currentChannelId,
            referencedMessageId);
    }

    private static async ValueTask<IReadOnlyList<ChatImageData>> GetReferencedChatImagesAsync(
        SocketMessage message,
        IChatImageRepository chatImageRepository,
        string currentChannelId)
    {
        var referencedMessageId = GetReferencedMessageId(message);
        if (string.IsNullOrWhiteSpace(referencedMessageId))
        {
            return [];
        }

        var referencedChannelId = GetReferencedChannelId(message) ?? currentChannelId;
        if (!string.Equals(referencedChannelId, currentChannelId, StringComparison.Ordinal))
        {
            return [];
        }

        return await chatImageRepository.GetByMessageIdsAsync(
            currentChannelId,
            [referencedMessageId],
            MaxReferencedImageCount);
    }

    private static string? GetReferencedMessageId(SocketMessage message)
    {
        return message.Reference?.MessageId.IsSpecified == true
            ? message.Reference.MessageId.Value.ToString()
            : null;
    }

    private static string? GetReferencedChannelId(SocketMessage message)
    {
        return message.Reference?.ChannelId.ToString();
    }

    private static string? GetReferencedGuildId(SocketMessage message)
    {
        return message.Reference?.GuildId.IsSpecified == true
            ? message.Reference.GuildId.Value.ToString()
            : null;
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
        IReadOnlyList<ChatLogImageInput>? images = null,
        IReadOnlyList<ChatLogAttachmentInput>? attachments = null,
        string? referencedMessageId = null,
        string? referencedChannelId = null,
        string? referencedGuildId = null)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IChatLogRepository>();
            await repository.AddAsync(
                messageId,
                guildId,
                channelId,
                userId,
                content,
                images,
                attachments,
                referencedMessageId,
                referencedChannelId,
                referencedGuildId);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to save chat log");
        }
    }
}
