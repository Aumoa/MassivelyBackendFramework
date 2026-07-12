using DiscordBot.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiscordBot.Tests.Services;

public sealed class DiscordAutoResponseCoordinatorTests
{
    [Fact]
    public async Task ObserveAsync_RecordsInitialSetupFailureWithMessageContext()
    {
        var settings = new FailingAutoResponseSettingsService(failOnGetCall: 1);
        using var coordinator = CreateCoordinator(settings);
        var message = CreateMessage();

        await coordinator.ObserveAsync(message, static (_, _) => Task.CompletedTask);

        var recorded = await settings.RecordedEvent.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var recordedMessage = Assert.Single(recorded.Messages);
        Assert.Equal(message.MessageId, recordedMessage.MessageId);
        Assert.Equal(message.ChannelId, recordedMessage.ChannelId);
        Assert.Equal("error", recorded.Decision);
        Assert.Equal("setup", recorded.Diagnostic?.Stage);
        Assert.Equal("Unexpected application failure.", recorded.Diagnostic?.ErrorMessage);
        Assert.DoesNotContain(
            "secret-token-value",
            recorded.Diagnostic?.ErrorMessage ?? string.Empty,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ObserveAsync_RecordsDelayedSetupFailureWithBufferedMessages()
    {
        var settings = new FailingAutoResponseSettingsService(failOnGetCall: 2);
        using var coordinator = CreateCoordinator(settings);
        var message = CreateMessage();

        await coordinator.ObserveAsync(message, static (_, _) => Task.CompletedTask);

        var recorded = await settings.RecordedEvent.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var recordedMessage = Assert.Single(recorded.Messages);
        Assert.Equal(message.MessageId, recordedMessage.MessageId);
        Assert.Equal(message.ChannelId, recordedMessage.ChannelId);
        Assert.Equal("error", recorded.Decision);
        Assert.Equal("setup", recorded.Diagnostic?.Stage);
        Assert.Equal("Unexpected application failure.", recorded.Diagnostic?.ErrorMessage);
        Assert.DoesNotContain(
            "secret-token-value",
            recorded.Diagnostic?.ErrorMessage ?? string.Empty,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ObserveAsync_DoesNotScheduleEvaluationWhenDisposedDuringSettingsLookup()
    {
        var settings = new BlockingAutoResponseSettingsService();
        var responseCallCount = 0;
        var coordinator = CreateCoordinator(settings);
        var observeTask = coordinator.ObserveAsync(
            CreateMessage(),
            (_, _) =>
            {
                Interlocked.Increment(ref responseCallCount);
                return Task.CompletedTask;
            }).AsTask();
        await settings.GetStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        coordinator.Dispose();
        settings.CompleteGet();
        await observeTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, settings.GetCallCount);
        Assert.Equal(0, responseCallCount);
    }

    private static DiscordAutoResponseCoordinator CreateCoordinator(
        IAutoResponseSettingsService settings)
    {
        return new DiscordAutoResponseCoordinator(
            new UnexpectedEvaluator(),
            settings,
            NullLogger<DiscordAutoResponseCoordinator>.Instance);
    }

    private static DiscordAutoResponseMessage CreateMessage()
    {
        return new DiscordAutoResponseMessage(
            "message-1",
            "guild-1",
            "channel-1",
            "user-1",
            "tester",
            "이 질문에 답할 수 있어?",
            DateTimeOffset.UtcNow,
            IsDirectMessage: false,
            MentionsBot: false,
            ReferencesBotMessage: false,
            HasAttachments: false);
    }

    private sealed class FailingAutoResponseSettingsService(int failOnGetCall)
        : IAutoResponseSettingsService
    {
        private int m_GetCallCount;

        public TaskCompletionSource<RecordedAutoResponseEvent> RecordedEvent { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<AutoResponseSettingsView> GetAsync(
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref m_GetCallCount) == failOnGetCall)
            {
                throw new InvalidOperationException("setup failed with secret-token-value");
            }

            return ValueTask.FromResult(new AutoResponseSettingsView(
                Enabled: true,
                IntervalSeconds: 45,
                CooldownSeconds: 180,
                MaxBufferedMessages: 20,
                ClassifierMaxTokens: 160,
                ClassifierModel: "classifier-model",
                BotNameAliases: ["봇"],
                CreatedAt: DateTime.UtcNow,
                UpdatedAt: null));
        }

        public ValueTask SaveAsync(
            AutoResponseSettingsSaveRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask RecordEventAsync(
            IReadOnlyList<DiscordAutoResponseMessage> messages,
            string decision,
            string reason,
            string focus,
            AutoResponseEventDiagnostic? diagnostic = null,
            CancellationToken cancellationToken = default)
        {
            RecordedEvent.TrySetResult(new RecordedAutoResponseEvent(
                messages,
                decision,
                reason,
                focus,
                diagnostic));
            return ValueTask.CompletedTask;
        }

        public ValueTask<IReadOnlyList<AutoResponseEventView>> GetRecentEventsAsync(
            int limit,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class UnexpectedEvaluator : IDiscordAutoResponseEvaluator
    {
        public ValueTask<DiscordAutoResponseDecision> EvaluateAsync(
            IReadOnlyList<DiscordAutoResponseMessage> messages,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Evaluator should not be called.");
        }
    }

    private sealed class BlockingAutoResponseSettingsService : IAutoResponseSettingsService
    {
        private readonly TaskCompletionSource<AutoResponseSettingsView> m_GetCompletion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int m_GetCallCount;

        public int GetCallCount => Volatile.Read(ref m_GetCallCount);

        public TaskCompletionSource GetStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<AutoResponseSettingsView> GetAsync(
            CancellationToken cancellationToken = default)
        {
            var callCount = Interlocked.Increment(ref m_GetCallCount);
            if (callCount == 1)
            {
                GetStarted.TrySetResult();
                return new ValueTask<AutoResponseSettingsView>(m_GetCompletion.Task);
            }

            return ValueTask.FromResult(CreateEnabledSettings());
        }

        public void CompleteGet()
        {
            m_GetCompletion.TrySetResult(CreateEnabledSettings());
        }

        public ValueTask SaveAsync(
            AutoResponseSettingsSaveRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask RecordEventAsync(
            IReadOnlyList<DiscordAutoResponseMessage> messages,
            string decision,
            string reason,
            string focus,
            AutoResponseEventDiagnostic? diagnostic = null,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("No event should be recorded after disposal.");
        }

        public ValueTask<IReadOnlyList<AutoResponseEventView>> GetRecentEventsAsync(
            int limit,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        private static AutoResponseSettingsView CreateEnabledSettings()
        {
            return new AutoResponseSettingsView(
                Enabled: true,
                IntervalSeconds: 1,
                CooldownSeconds: 180,
                MaxBufferedMessages: 20,
                ClassifierMaxTokens: 160,
                ClassifierModel: "classifier-model",
                BotNameAliases: ["봇"],
                CreatedAt: DateTime.UtcNow,
                UpdatedAt: null);
        }
    }

    private sealed record RecordedAutoResponseEvent(
        IReadOnlyList<DiscordAutoResponseMessage> Messages,
        string Decision,
        string Reason,
        string Focus,
        AutoResponseEventDiagnostic? Diagnostic);
}
