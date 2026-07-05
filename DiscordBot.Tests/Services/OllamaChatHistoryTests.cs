using System.Runtime.CompilerServices;
using AI;
using Discord;
using DiscordBot.Repositories;
using DiscordBot.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiscordBot.Tests.Services;

public sealed class OllamaChatHistoryTests
{
    [Fact]
    public void GetDefaultBehaviorInstruction_PrefersChannelChatLookupForNaturalPastChatReferences()
    {
        var instruction = OllamaChatHistory.GetDefaultBehaviorInstruction();

        Assert.Contains("현재 Discord 채널의 과거 채팅", instruction);
        Assert.Contains("기억이나 추측으로 답하지 말고 먼저 현재 채널의 채팅 조회 도구를 사용하세요", instruction);
        Assert.Contains("과거 지시어를 쓰지 않아도", instruction);
        Assert.Contains("현재 입력만으로 전제가 설명되지 않는 이전 발화", instruction);
        Assert.Contains("먼저 현재 채널의 채팅 조회 도구로 실제 기록을 확인하세요", instruction);
        Assert.Contains("어떤 근거로", instruction);
        Assert.Contains("히스토리 밖 대화 가능성", instruction);
        Assert.Contains("전제가 현재 대화 문맥에 있는 것처럼 보이더라도 기억만으로 근거를 단정하지 마세요", instruction);
        Assert.Contains("AI와 나눈 직전 대화 자체", instruction);
        Assert.Contains("문장 의미나 표현만 명확히 묻고", instruction);
        Assert.Contains("근거/출처/참조/이전 분석의 이유를 묻는 질문", instruction);
        Assert.Contains("search_chat_history", instruction);
        Assert.Contains("get_chat_context", instruction);
        Assert.Contains("get_chat_by_message_id", instruction);
    }

    [Fact]
    public async Task AddAsync_RemembersNormalConversationTurns()
    {
        var chatClient = new CapturingChatClient();
        var history = CreateHistory(chatClient);
        var user = new FakeUser();

        await DrainAsync(history.AddAsync(user, "first prompt", ToolsProvider.CreateFrom()));
        await DrainAsync(history.AddAsync(user, "second prompt", ToolsProvider.CreateFrom()));

        Assert.Equal(2, chatClient.Calls.Count);
        Assert.Contains(chatClient.Calls[1], message => message.Content.Contains("first prompt", StringComparison.Ordinal));
        Assert.Contains(chatClient.Calls[1], message => message.Content.Contains("response 1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AddAsync_DoesNotRememberNoMemoryTurns()
    {
        var chatClient = new CapturingChatClient();
        var history = CreateHistory(chatClient);
        var user = new FakeUser();

        await DrainAsync(history.AddAsync(
            user,
            "[자동 응답 모드] synthetic batch",
            ToolsProvider.CreateFrom(),
            rememberConversation: false));
        await DrainAsync(history.AddAsync(user, "normal prompt", ToolsProvider.CreateFrom()));

        Assert.Equal(2, chatClient.Calls.Count);
        Assert.DoesNotContain(
            chatClient.Calls[1],
            message => message.Content.Contains("synthetic batch", StringComparison.Ordinal));
        Assert.DoesNotContain(
            chatClient.Calls[1],
            message => message.Content.Contains("response 1", StringComparison.Ordinal));
        Assert.Contains(
            chatClient.Calls[1],
            message => message.Content.Contains("normal prompt", StringComparison.Ordinal));
    }

    private static OllamaChatHistory CreateHistory(CapturingChatClient chatClient)
    {
        return new OllamaChatHistory(
            NullLogger.Instance,
            new OllamaService.Configuration
            {
                MemorySize = 20,
                Model = "model",
                SummaryModel = "summary"
            },
            chatClient,
            new FakeClaudeSettingsService(),
            new FakeAiSkillProvider());
    }

    private static async Task DrainAsync(IAsyncEnumerable<DiscordBot.Services.ChatResponseChunk> chunks)
    {
        await foreach (var _ in chunks)
        {
        }
    }

    private sealed class CapturingChatClient : IChatClient
    {
        public List<IReadOnlyList<ChatMessage>> Calls { get; } = [];

        public async IAsyncEnumerable<AI.ChatResponseChunk> ChatAsync(
            IReadOnlyList<ChatMessage> messages,
            ChatCompletionOptions options,
            IReadOnlyList<ToolFunctionDescription>? tools = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Calls.Add(messages.ToArray());
            await Task.Yield();
            yield return new AI.ChatResponseChunk
            {
                Content = $"response {Calls.Count}"
            };
        }

        public Task<string> GenerateAsync(
            string prompt,
            ChatCompletionOptions options,
            string? system = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult("summary");
        }
    }

    private sealed class FakeClaudeSettingsService : IClaudeSettingsService
    {
        public ValueTask<ClaudeSettingsData> GetAsync(CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(new ClaudeSettingsData(
                "model",
                "summary",
                4096,
                string.Empty,
                new DateTime(2026, 7, 5),
                null));
        }

        public ValueTask SaveAsync(
            string model,
            string summaryModel,
            int defaultMaxTokens,
            string instructions,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeAiSkillProvider : IAiSkillProvider
    {
        public ValueTask<IReadOnlyList<AiSkillDefinition>> GetActiveSkillsAsync(CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult<IReadOnlyList<AiSkillDefinition>>([]);
        }

        public ValueTask<AiSkillSelection> SelectSkillsAsync(
            string prompt,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(new AiSkillSelection([], new HashSet<string>(StringComparer.Ordinal)));
        }
    }

    private sealed class FakeUser : IUser
    {
        public string? AvatarId => null;

        public string Discriminator => "0001";

        public ushort DiscriminatorValue => 1;

        public bool IsBot => false;

        public bool IsWebhook => false;

        public string Username => "tester";

        public UserProperties? PublicFlags => null;

        public string? GlobalName => null;

        public string? AvatarDecorationHash => null;

        public ulong? AvatarDecorationSkuId => null;

        public PrimaryGuild? PrimaryGuild => null;

        public DateTimeOffset CreatedAt => DateTimeOffset.UnixEpoch;

        public ulong Id => 1;

        public string Mention => "<@1>";

        public UserStatus Status => UserStatus.Online;

        public IReadOnlyCollection<ClientType> ActiveClients => [];

        public IReadOnlyCollection<IActivity> Activities => [];

        public string GetAvatarUrl(ImageFormat format = ImageFormat.Auto, ushort size = 128)
        {
            return string.Empty;
        }

        public string GetDefaultAvatarUrl()
        {
            return string.Empty;
        }

        public string GetDisplayAvatarUrl(ImageFormat format = ImageFormat.Auto, ushort size = 128)
        {
            return string.Empty;
        }

        public Task<IDMChannel> CreateDMChannelAsync(RequestOptions? options = null)
        {
            throw new NotSupportedException();
        }

        public string GetAvatarDecorationUrl()
        {
            return string.Empty;
        }
    }
}
