using AI;
using DiscordBot.Repositories;
using DiscordBot.Services;
using DiscordBot.Services.ImageGeneration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiscordBot.Tests.Services;

public sealed class DailyImagePostRunnerTests
{
    [Fact]
    public async Task RunOnceAsync_SkipsImageGeneration_WhenDisabledAndNotForced()
    {
        var imageGenerationClient = new FakeImageGenerationClient();
        var runner = CreateRunner(
            settingsView: CreateSettingsView(enabled: false),
            imageGenerationClient: imageGenerationClient);

        var result = await runner.RunOnceAsync(force: false, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("disabled", result.Reason);
        Assert.False(imageGenerationClient.WasCalled);
    }

    [Fact]
    public async Task RunOnceAsync_RunsEvenWhenDisabled_WhenForced()
    {
        var imageGenerationClient = new FakeImageGenerationClient();
        var runner = CreateRunner(
            settingsView: CreateSettingsView(enabled: false),
            imageGenerationClient: imageGenerationClient);

        var result = await runner.RunOnceAsync(force: true, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(imageGenerationClient.WasCalled);
    }

    [Fact]
    public async Task RunOnceAsync_SkipsImageGeneration_WhenChannelIdIsNotNumeric()
    {
        var imageGenerationClient = new FakeImageGenerationClient();
        var runner = CreateRunner(
            settingsView: CreateSettingsView(channelId: "not-a-number"),
            imageGenerationClient: imageGenerationClient);

        var result = await runner.RunOnceAsync(force: true, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("invalid_channel", result.Reason);
        Assert.False(imageGenerationClient.WasCalled);
    }

    [Fact]
    public async Task RunOnceAsync_SkipsImageGeneration_WhenChannelSenderNeverConnects()
    {
        var imageGenerationClient = new FakeImageGenerationClient();
        var channelSender = new FakeDiscordChannelSender { WaitForConnectionResult = false };
        var runner = CreateRunner(
            settingsView: CreateSettingsView(),
            imageGenerationClient: imageGenerationClient,
            channelSender: channelSender);

        var result = await runner.RunOnceAsync(force: true, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("not_connected", result.Reason);
        Assert.False(imageGenerationClient.WasCalled);
    }

    [Fact]
    public async Task RunOnceAsync_UsesLlmConceptAndCaption_WhenChatClientReturnsValidJson()
    {
        var imageGenerationClient = new FakeImageGenerationClient();
        var channelSender = new FakeDiscordChannelSender();
        var chatClient = new FakeChatClient
        {
            Response = """{"positive_prompt":"blue hair, standing pose","caption":"오늘은 파란 머리로 그려봤어요."}"""
        };
        var runner = CreateRunner(
            settingsView: CreateSettingsView(),
            imageGenerationClient: imageGenerationClient,
            channelSender: channelSender,
            chatClient: chatClient);

        var result = await runner.RunOnceAsync(force: true, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("blue hair, standing pose", imageGenerationClient.LastPositivePrompt);
        Assert.Equal("🖼️ 오늘의 이미지\n오늘은 파란 머리로 그려봤어요.", channelSender.LastText);
    }

    [Fact]
    public async Task RunOnceAsync_FallsBackToStaticPromptAndCaption_WhenChatClientThrows()
    {
        var imageGenerationClient = new FakeImageGenerationClient();
        var channelSender = new FakeDiscordChannelSender();
        var chatClient = new FakeChatClient { ShouldThrow = true };
        var runner = CreateRunner(
            settingsView: CreateSettingsView(themePrompt: "우주"),
            imageGenerationClient: imageGenerationClient,
            channelSender: channelSender,
            chatClient: chatClient);

        var result = await runner.RunOnceAsync(force: true, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("우주", imageGenerationClient.LastPositivePrompt);
        Assert.Equal("🖼️ 오늘의 이미지\n오늘의 이미지를 그려봤어요.", channelSender.LastText);
    }

    [Fact]
    public async Task RunOnceAsync_FallsBackToStaticPromptAndCaption_WhenChatClientReturnsUnparsableText()
    {
        var imageGenerationClient = new FakeImageGenerationClient();
        var channelSender = new FakeDiscordChannelSender();
        var chatClient = new FakeChatClient { Response = "이건 JSON이 아닙니다" };
        var runner = CreateRunner(
            settingsView: CreateSettingsView(themePrompt: ""),
            imageGenerationClient: imageGenerationClient,
            channelSender: channelSender,
            chatClient: chatClient);

        var result = await runner.RunOnceAsync(force: true, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("", imageGenerationClient.LastPositivePrompt);
        Assert.Equal("🖼️ 오늘의 이미지\n오늘의 이미지를 그려봤어요.", channelSender.LastText);
    }

    [Fact]
    public async Task RunOnceAsync_UsesFallbackPromptButLlmCaption_WhenPositivePromptIsEmpty()
    {
        var imageGenerationClient = new FakeImageGenerationClient();
        var channelSender = new FakeDiscordChannelSender();
        var chatClient = new FakeChatClient
        {
            Response = """{"positive_prompt":"","caption":"그냥 코멘트만 왔어요."}"""
        };
        var runner = CreateRunner(
            settingsView: CreateSettingsView(themePrompt: "바다"),
            imageGenerationClient: imageGenerationClient,
            channelSender: channelSender,
            chatClient: chatClient);

        var result = await runner.RunOnceAsync(force: true, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("바다", imageGenerationClient.LastPositivePrompt);
        Assert.Equal("🖼️ 오늘의 이미지\n그냥 코멘트만 왔어요.", channelSender.LastText);
    }

    [Fact]
    public async Task RunOnceAsync_PassesThemePromptIntoChatClientUserMessage()
    {
        var chatClient = new FakeChatClient { ShouldThrow = true };
        var runner = CreateRunner(
            settingsView: CreateSettingsView(themePrompt: "겨울 밤"),
            chatClient: chatClient);

        await runner.RunOnceAsync(force: true, CancellationToken.None);

        Assert.Contains("겨울 밤", chatClient.LastPrompt);
    }

    private static DailyImagePostRunner CreateRunner(
        DailyImagePostSettingsView settingsView,
        FakeImageGenerationClient? imageGenerationClient = null,
        FakeDiscordChannelSender? channelSender = null,
        FakeChatClient? chatClient = null)
    {
        var promptProfileProvider = new ImagePromptProfileProvider(
            Microsoft.Extensions.Options.Options.Create(new DiscordBot.Options.ImageGenerationOptions
            {
                PromptProfilePath = "nonexistent-prompt-profile.json"
            }),
            new FakeImageGenerationWorkflowRepository(),
            new FakeHostEnvironment(),
            NullLogger<ImagePromptProfileProvider>.Instance);

        return new DailyImagePostRunner(
            new FakeDailyImagePostSettingsService(settingsView),
            imageGenerationClient ?? new FakeImageGenerationClient(),
            promptProfileProvider,
            chatClient ?? new FakeChatClient { ShouldThrow = true },
            new FakeClaudeSettingsService(),
            channelSender ?? new FakeDiscordChannelSender(),
            NullLogger<DailyImagePostRunner>.Instance);
    }

    private static DailyImagePostSettingsView CreateSettingsView(
        bool enabled = true,
        string channelId = "123456789012345678",
        string themePrompt = "")
    {
        return new DailyImagePostSettingsView(
            enabled,
            channelId,
            "09:00",
            themePrompt,
            DateTime.Now,
            null);
    }

    private sealed class FakeDailyImagePostSettingsService(DailyImagePostSettingsView view) : IDailyImagePostSettingsService
    {
        public ValueTask<DailyImagePostSettingsView> GetAsync(CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(view);
        }

        public ValueTask SaveAsync(
            DailyImagePostSettingsSaveRequest request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FakeImageGenerationClient : IImageGenerationClient
    {
        public bool WasCalled { get; private set; }

        public string? LastPositivePrompt { get; private set; }

        public Task<GeneratedImage> GenerateAsync(
            string positivePrompt,
            IProgress<ImageGenerationProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            LastPositivePrompt = positivePrompt;
            return Task.FromResult(new GeneratedImage([1, 2, 3], "image.png", "image/png"));
        }
    }

    private sealed class FakeDiscordChannelSender : IDiscordChannelSender
    {
        public bool WaitForConnectionResult { get; set; } = true;

        public bool SendFileResult { get; set; } = true;

        public string? LastText { get; private set; }

        public Task<bool> WaitForConnectionAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            return Task.FromResult(WaitForConnectionResult);
        }

        public Task<bool> SendFileAsync(
            ulong channelId, Stream stream, string fileName, string? text, CancellationToken cancellationToken)
        {
            LastText = text;
            return Task.FromResult(SendFileResult);
        }
    }

    private sealed class FakeChatClient : IChatClient
    {
        public string Response { get; set; } = "";

        public bool ShouldThrow { get; set; }

        public string? LastPrompt { get; private set; }

        public IAsyncEnumerable<AI.ChatResponseChunk> ChatAsync(
            IReadOnlyList<ChatMessage> messages,
            ChatCompletionOptions options,
            IReadOnlyList<ToolFunctionDescription>? tools = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<string> GenerateAsync(
            string prompt, ChatCompletionOptions options, string? system = null, CancellationToken cancellationToken = default)
        {
            LastPrompt = prompt;
            if (ShouldThrow)
            {
                throw new InvalidOperationException("simulated chat client failure");
            }

            return Task.FromResult(Response);
        }
    }

    private sealed class FakeClaudeSettingsService : IClaudeSettingsService
    {
        public ValueTask<ClaudeSettingsData> GetAsync(CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(new ClaudeSettingsData(
                "model", "summary-model", 1024, "너는 친절한 디스코드 봇이다.", DateTime.Now, null));
        }

        public ValueTask SaveAsync(
            string model, string summaryModel, int defaultMaxTokens, string instructions,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FakeImageGenerationWorkflowRepository : IImageGenerationWorkflowRepository
    {
        public ValueTask<IReadOnlyList<ImageGenerationWorkflowData>> ListAsync(CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<ImageGenerationWorkflowData?> GetAsync(string name, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<ImageGenerationWorkflowData?> GetFirstAsync(CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult<ImageGenerationWorkflowData?>(null);
        }

        public ValueTask UpsertAsync(
            string name, string? description, string workflowJson, int sortOrder,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask RenameAsync(string oldName, string newName, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask DeleteAsync(string name, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public string ApplicationName { get; set; } = "DiscordBot.Tests";

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public string EnvironmentName { get; set; } = "Test";
    }
}
