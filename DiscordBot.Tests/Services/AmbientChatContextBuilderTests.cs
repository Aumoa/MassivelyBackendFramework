using DiscordBot.Options;
using DiscordBot.Repositories;
using DiscordBot.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiscordBot.Tests.Services;

public sealed class AmbientChatContextBuilderTests
{
    private static readonly AmbientChatContextOptions DefaultOptions = new()
    {
        Enabled = true,
        WindowMessageCount = 12,
        WindowMaxChars = 2000,
        LookbackMinutes = 60
    };

    [Fact]
    public void FormatBlock_ReturnsNull_WhenNoRows()
    {
        var block = AmbientChatContextBuilder.FormatBlock(
            [], DateTimeOffset.UtcNow, "self-id", DefaultOptions);

        Assert.Null(block);
    }

    [Fact]
    public void FormatBlock_ComputesSilenceMinutesAndDensityFromTimestamps()
    {
        var rows = new[]
        {
            CreateRow(1, "user-1", "첫 메시지", new DateTime(2026, 8, 23, 11, 40, 0, DateTimeKind.Utc)),
            CreateRow(2, "user-1", "마지막 메시지", new DateTime(2026, 8, 23, 12, 0, 0, DateTimeKind.Utc))
        };
        var current = new DateTimeOffset(2026, 8, 23, 12, 15, 0, TimeSpan.Zero);

        var block = AmbientChatContextBuilder.FormatBlock(rows, current, "self-id", DefaultOptions);

        Assert.NotNull(block);
        Assert.Contains("경과 시간: 약 15분", block);
        Assert.Contains("최근 20분 동안 메시지 2건", block);
    }

    [Fact]
    public void FormatBlock_LabelsSelfUserRowsAsMyResponse()
    {
        var rows = new[]
        {
            CreateRow(1, "self-id", "봇의 이전 응답", new DateTime(2026, 8, 23, 12, 0, 0, DateTimeKind.Utc))
        };

        var block = AmbientChatContextBuilder.FormatBlock(
            rows, new DateTimeOffset(2026, 8, 23, 12, 5, 0, TimeSpan.Zero), "self-id", DefaultOptions);

        Assert.NotNull(block);
        Assert.Contains("(나의 응답): 봇의 이전 응답", block);
    }

    [Fact]
    public void FormatBlock_TruncatesFromOldest_ButAlwaysKeepsMostRecentLine()
    {
        var rows = new[]
        {
            CreateRow(1, "user-1", new string('a', 100), new DateTime(2026, 8, 23, 11, 0, 0, DateTimeKind.Utc)),
            CreateRow(2, "user-1", new string('b', 100), new DateTime(2026, 8, 23, 11, 30, 0, DateTimeKind.Utc)),
            CreateRow(3, "user-1", new string('c', 100), new DateTime(2026, 8, 23, 12, 0, 0, DateTimeKind.Utc))
        };
        var options = CreateOptions(windowMaxChars: 200);

        var block = AmbientChatContextBuilder.FormatBlock(
            rows, new DateTimeOffset(2026, 8, 23, 12, 5, 0, TimeSpan.Zero), "self-id", options);

        Assert.NotNull(block);
        Assert.Contains(new string('c', 100), block);
        Assert.DoesNotContain(new string('a', 100), block);
    }

    [Fact]
    public void FormatBlock_KeepsMostRecentLine_EvenIfAloneItExceedsMaxChars()
    {
        var rows = new[]
        {
            CreateRow(1, "user-1", new string('z', 500), new DateTime(2026, 8, 23, 12, 0, 0, DateTimeKind.Utc))
        };
        var options = CreateOptions(windowMaxChars: 200);

        var block = AmbientChatContextBuilder.FormatBlock(
            rows, new DateTimeOffset(2026, 8, 23, 12, 5, 0, TimeSpan.Zero), "self-id", options);

        Assert.NotNull(block);
        Assert.Contains(new string('z', 500), block);
    }

    [Fact]
    public async Task BuildAsync_ReturnsNull_WhenDisabled()
    {
        var repository = new FakeChatLogRepository(
        [
            CreateRow(1, "user-1", "메시지", new DateTime(2026, 8, 23, 12, 0, 0, DateTimeKind.Utc))
        ]);
        var request = new AmbientChatContextRequest(
            repository, "channel-1", "self-id", null, new DateTimeOffset(2026, 8, 23, 12, 5, 0, TimeSpan.Zero));

        var message = await AmbientChatContextBuilder.BuildAsync(
            request, CreateOptions(enabled: false), NullLogger.Instance, CancellationToken.None);

        Assert.Null(message);
        Assert.False(repository.WasQueried);
    }

    [Fact]
    public async Task BuildAsync_PassesLookbackAndLimitToRepository()
    {
        var repository = new FakeChatLogRepository([]);
        var current = new DateTimeOffset(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);
        var request = new AmbientChatContextRequest(repository, "channel-1", "self-id", null, current);
        var options = CreateOptions(windowMessageCount: 5, lookbackMinutes: 30);

        await AmbientChatContextBuilder.BuildAsync(request, options, NullLogger.Instance, CancellationToken.None);

        Assert.True(repository.WasQueried);
        Assert.Equal("channel-1", repository.LastChannelId);
        Assert.Equal(5, repository.LastLimit);
        Assert.Equal(current - TimeSpan.FromMinutes(30), repository.LastFrom);
        Assert.Equal(current, repository.LastTo);
    }

    [Fact]
    public async Task BuildAsync_FiltersOutExcludedMessageId()
    {
        var repository = new FakeChatLogRepository(
        [
            CreateRow(1, "user-1", "제외될 메시지", new DateTime(2026, 8, 23, 12, 0, 0, DateTimeKind.Utc), messageId: "current-message"),
            CreateRow(2, "user-1", "남을 메시지", new DateTime(2026, 8, 23, 12, 1, 0, DateTimeKind.Utc), messageId: "other-message")
        ]);
        var request = new AmbientChatContextRequest(
            repository, "channel-1", "self-id", "current-message", new DateTimeOffset(2026, 8, 23, 12, 5, 0, TimeSpan.Zero));

        var message = await AmbientChatContextBuilder.BuildAsync(
            request, DefaultOptions, NullLogger.Instance, CancellationToken.None);

        Assert.NotNull(message);
        Assert.DoesNotContain("제외될 메시지", message.Content);
        Assert.Contains("남을 메시지", message.Content);
    }

    private static AmbientChatContextOptions CreateOptions(
        bool enabled = true,
        int windowMessageCount = 12,
        int windowMaxChars = 2000,
        int lookbackMinutes = 60)
    {
        return new AmbientChatContextOptions
        {
            Enabled = enabled,
            WindowMessageCount = windowMessageCount,
            WindowMaxChars = windowMaxChars,
            LookbackMinutes = lookbackMinutes
        };
    }

    private static ChatLogData CreateRow(
        long id,
        string userId,
        string content,
        DateTime createdAt,
        string? messageId = null)
    {
        return new ChatLogData(id, messageId ?? $"msg-{id}", null, "channel-1", userId, content, createdAt);
    }

    private sealed class FakeChatLogRepository(IReadOnlyList<ChatLogData> rows) : IChatLogRepository
    {
        public bool WasQueried { get; private set; }

        public string? LastChannelId { get; private set; }

        public int LastLimit { get; private set; }

        public DateTimeOffset? LastFrom { get; private set; }

        public DateTimeOffset? LastTo { get; private set; }

        public ValueTask AddAsync(
            string? messageId,
            string? guildId,
            string channelId,
            string userId,
            string content,
            IReadOnlyList<ChatLogImageInput>? images = null,
            IReadOnlyList<ChatLogAttachmentInput>? attachments = null,
            string? referencedMessageId = null,
            string? referencedChannelId = null,
            string? referencedGuildId = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<IReadOnlyList<ChatLogData>> GetAsync(
            string channelId,
            int limit,
            int offset = 0,
            DateTimeOffset? from = null,
            DateTimeOffset? to = null,
            CancellationToken cancellationToken = default)
        {
            WasQueried = true;
            LastChannelId = channelId;
            LastLimit = limit;
            LastFrom = from;
            LastTo = to;
            return ValueTask.FromResult(rows);
        }

        public ValueTask<IReadOnlyList<ChatLogData>> SearchAsync(
            string channelId,
            IReadOnlyList<string> keywords,
            int limit,
            DateTimeOffset? from = null,
            DateTimeOffset? to = null,
            string? authorUserId = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<IReadOnlyList<ChatLogData>> GetContextAsync(
            string channelId,
            long chatLogId,
            int before,
            int after,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<ChatLogData?> GetByMessageIdAsync(
            string channelId,
            string messageId,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<IReadOnlyList<ChatLogData>> GetRepliesAsync(
            string channelId,
            string referencedMessageId,
            int limit,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
