using System.Net;
using System.Text.Json;
using DiscordBot.Options;
using DiscordBot.Repositories;
using DiscordBot.Services;
using Microsoft.Extensions.Caching.Memory;

namespace DiscordBot.Tests.Services;

public sealed class AutoResponseSettingsServiceTests
{
    [Fact]
    public async Task GetAsync_SeedsDefaultSettingsFromOptions()
    {
        var repository = new FakeAutoResponseRepository();
        var options = new AutoResponseOptions
        {
            Enabled = true,
            IntervalSeconds = 30,
            CooldownSeconds = 120,
            MaxBufferedMessages = 12,
            ClassifierMaxTokens = 96,
            ClassifierModel = "classifier-model",
            BotNameAliases = ["bot", "AI"]
        };
        var service = CreateService(repository, options);

        var settings = await service.GetAsync();

        Assert.True(settings.Enabled);
        Assert.Equal(30, settings.IntervalSeconds);
        Assert.Equal(120, settings.CooldownSeconds);
        Assert.Equal(12, settings.MaxBufferedMessages);
        Assert.Equal(96, settings.ClassifierMaxTokens);
        Assert.Equal("classifier-model", settings.ClassifierModel);
        Assert.Equal(["bot", "AI"], settings.BotNameAliases);
        Assert.NotNull(repository.Settings);
    }

    [Fact]
    public async Task SaveAsync_NormalizesRangesAndAliases()
    {
        var repository = new FakeAutoResponseRepository();
        var service = CreateService(repository, new AutoResponseOptions
        {
            IntervalSeconds = 45,
            CooldownSeconds = 180,
            MaxBufferedMessages = 20,
            ClassifierMaxTokens = 160
        });

        await service.SaveAsync(new AutoResponseSettingsSaveRequest(
            true,
            9999,
            9999,
            9999,
            9999,
            " summary ",
            [" bot ", "bot", "", "AI"]));

        Assert.NotNull(repository.Settings);
        Assert.True(repository.Settings.Enabled);
        Assert.Equal(600, repository.Settings.IntervalSeconds);
        Assert.Equal(3600, repository.Settings.CooldownSeconds);
        Assert.Equal(50, repository.Settings.MaxBufferedMessages);
        Assert.Equal(512, repository.Settings.ClassifierMaxTokens);
        Assert.Equal("summary", repository.Settings.ClassifierModel);

        var aliases = JsonSerializer.Deserialize<string[]>(repository.Settings.BotNameAliasesJson);
        Assert.NotNull(aliases);
        Assert.Equal(["bot", "AI"], aliases);
    }

    [Fact]
    public async Task RecordEventAsync_StoresChannelScopedMessageIds()
    {
        var repository = new FakeAutoResponseRepository();
        var service = CreateService(repository);
        var messages =
            new[]
            {
                CreateMessage("message-1", "channel-1", new DateTimeOffset(2026, 7, 5, 10, 0, 0, TimeSpan.Zero)),
                CreateMessage("message-2", "channel-1", new DateTimeOffset(2026, 7, 5, 10, 1, 0, TimeSpan.Zero))
            };

        await service.RecordEventAsync(messages, "accepted", "bot_alias", "answer briefly");

        var ev = Assert.Single(repository.Events);
        Assert.Equal("guild-1", ev.GuildId);
        Assert.Equal("channel-1", ev.ChannelId);
        Assert.Equal("message-2", ev.TriggerMessageId);
        Assert.Equal("accepted", ev.Decision);
        Assert.Equal("bot_alias", ev.Reason);
        Assert.Equal("answer briefly", ev.Focus);
        Assert.Equal(string.Empty, ev.ErrorStage);
        Assert.Null(ev.HttpStatusCode);
        Assert.Equal(string.Empty, ev.ErrorMessage);
        var messageIds = JsonSerializer.Deserialize<string[]>(ev.MessageIdsJson);
        Assert.NotNull(messageIds);
        Assert.Equal(["message-1", "message-2"], messageIds);
    }

    [Fact]
    public async Task RecordEventAsync_DoesNotStoreSensitiveExceptionMessage()
    {
        var repository = new FakeAutoResponseRepository();
        var service = CreateService(repository);
        const string SECRET = "secret-token-value";
        var exception = new HttpRequestException(
            $"Claude API 429 response included {SECRET}",
            inner: null,
            HttpStatusCode.TooManyRequests);

        await service.RecordEventAsync(
            [CreateMessage("message-1", "channel-1", DateTimeOffset.UtcNow)],
            "error",
            nameof(HttpRequestException),
            string.Empty,
            AutoResponseEventDiagnostic.FromException("classifier", exception));

        var ev = Assert.Single(repository.Events);
        Assert.Equal("channel-1", ev.ChannelId);
        Assert.Equal("classifier", ev.ErrorStage);
        Assert.Equal(429, ev.HttpStatusCode);
        Assert.Equal("HTTP request failed with status 429.", ev.ErrorMessage);
        Assert.DoesNotContain(SECRET, ev.ErrorMessage, StringComparison.Ordinal);
    }

    private static AutoResponseSettingsService CreateService(
        FakeAutoResponseRepository repository,
        AutoResponseOptions? options = null)
    {
        return new AutoResponseSettingsService(
            repository,
            repository,
            Microsoft.Extensions.Options.Options.Create(options ?? new AutoResponseOptions()),
            new MemoryCache(new MemoryCacheOptions()));
    }

    private static DiscordAutoResponseMessage CreateMessage(
        string messageId,
        string channelId,
        DateTimeOffset timestamp)
    {
        return new DiscordAutoResponseMessage(
            messageId,
            "guild-1",
            channelId,
            "user-1",
            "tester",
            "봇이 이거 할 수 있나?",
            timestamp,
            IsDirectMessage: false,
            MentionsBot: false,
            ReferencesBotMessage: false,
            HasAttachments: false);
    }

    private sealed class FakeAutoResponseRepository :
        IAutoResponseSettingsRepository,
        IAutoResponseEventRepository
    {
        public AutoResponseSettingsData? Settings { get; private set; }

        public List<AutoResponseEventInput> Events { get; } = [];

        public ValueTask<AutoResponseSettingsData?> GetAsync(CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(Settings);
        }

        public ValueTask UpsertAsync(
            bool enabled,
            int intervalSeconds,
            int cooldownSeconds,
            int maxBufferedMessages,
            int classifierMaxTokens,
            string? classifierModel,
            string botNameAliasesJson,
            CancellationToken cancellationToken = default)
        {
            Settings = new AutoResponseSettingsData(
                enabled,
                intervalSeconds,
                cooldownSeconds,
                maxBufferedMessages,
                classifierMaxTokens,
                classifierModel,
                botNameAliasesJson,
                DateTime.Now,
                DateTime.Now);
            return ValueTask.CompletedTask;
        }

        public ValueTask AddAsync(
            AutoResponseEventInput input,
            CancellationToken cancellationToken = default)
        {
            Events.Add(input);
            return ValueTask.CompletedTask;
        }

        public ValueTask<IReadOnlyList<AutoResponseEventData>> GetRecentAsync(
            int limit,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult<IReadOnlyList<AutoResponseEventData>>([]);
        }
    }
}
