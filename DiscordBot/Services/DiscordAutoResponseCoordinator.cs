using DiscordBot.Options;

namespace DiscordBot.Services;

internal sealed record DiscordAutoResponseRequest(
    IReadOnlyList<DiscordAutoResponseMessage> Messages,
    DiscordAutoResponseDecision Decision);

internal interface IDiscordAutoResponseCoordinator
{
    ValueTask ObserveAsync(
        DiscordAutoResponseMessage message,
        Func<DiscordAutoResponseRequest, CancellationToken, Task> respondAsync);

    void CancelPending(string channelId);
}

internal sealed class DiscordAutoResponseCoordinator(
    IDiscordAutoResponseEvaluator evaluator,
    IAutoResponseSettingsService autoResponseSettings,
    ILogger<DiscordAutoResponseCoordinator> logger) : IDiscordAutoResponseCoordinator, IDisposable
{
    private const int DefaultIntervalSeconds = 45;
    private const int MaxIntervalSeconds = 600;
    private const int DefaultCooldownSeconds = 180;
    private const int MaxCooldownSeconds = 3600;
    private const int DefaultMaxBufferedMessages = 20;
    private const int MaxBufferedMessagesLimit = 50;

    private readonly object m_Lock = new();
    private readonly Dictionary<string, ChannelState> m_Channels = [];
    private bool m_Disposed;

    public async ValueTask ObserveAsync(
        DiscordAutoResponseMessage message,
        Func<DiscordAutoResponseRequest, CancellationToken, Task> respondAsync)
    {
        if (message.IsDirectMessage || message.MentionsBot || m_Disposed)
        {
            return;
        }

        AutoResponseOptions currentOptions;
        try
        {
            currentOptions = (await autoResponseSettings.GetAsync()).ToOptions();
        }
        catch (Exception e)
        {
            var diagnostic = AutoResponseEventDiagnostic.FromException("setup", e);
            LogAutoResponseFailure(message.ChannelId, e.GetType().Name, diagnostic);
            await RecordEventAsync(
                [message],
                "error",
                e.GetType().Name,
                string.Empty,
                CancellationToken.None,
                diagnostic);
            return;
        }

        if (!currentOptions.Enabled)
        {
            return;
        }

        lock (m_Lock)
        {
            if (m_Disposed)
            {
                return;
            }

            if (!m_Channels.TryGetValue(message.ChannelId, out var state))
            {
                state = new ChannelState();
                m_Channels.Add(message.ChannelId, state);
            }

            if (state.IsEvaluating)
            {
                state.EvaluationVersion++;
                state.IsEvaluating = false;
            }

            var hasPendingEvaluation = state.DelayCts != null;
            var startsEvaluation = DiscordAutoResponseEvaluator.PassesStaticFilter(message, currentOptions);
            if (!hasPendingEvaluation && !startsEvaluation)
            {
                return;
            }

            AddMessage(state, message, currentOptions);
            state.RespondAsync = respondAsync;

            if (hasPendingEvaluation)
            {
                return;
            }

            state.DelayCts = new CancellationTokenSource();
            state.EvaluationVersion++;
            _ = RunEvaluationAfterDelayAsync(
                message.ChannelId,
                state.EvaluationVersion,
                state.DelayCts);
        }
    }

    public void CancelPending(string channelId)
    {
        CancellationTokenSource? delayCts = null;
        lock (m_Lock)
        {
            if (!m_Channels.TryGetValue(channelId, out var state))
            {
                return;
            }

            delayCts = state.DelayCts;
            state.DelayCts = null;
            state.Messages.Clear();
            state.RespondAsync = null;
            state.EvaluationVersion++;
            state.IsEvaluating = false;
        }

        delayCts?.Cancel();
    }

    public void Dispose()
    {
        List<CancellationTokenSource> cancellationTokens = [];
        lock (m_Lock)
        {
            if (m_Disposed)
            {
                return;
            }

            m_Disposed = true;
            foreach (var state in m_Channels.Values)
            {
                if (state.DelayCts != null)
                {
                    cancellationTokens.Add(state.DelayCts);
                }

                state.DelayCts = null;
                state.Messages.Clear();
                state.RespondAsync = null;
            }
        }

        foreach (var cts in cancellationTokens)
        {
            cts.Cancel();
        }
    }

    private async Task RunEvaluationAfterDelayAsync(
        string channelId,
        int evaluationVersion,
        CancellationTokenSource delayCts)
    {
        var cancellationToken = delayCts.Token;
        IReadOnlyList<DiscordAutoResponseMessage> batch = [];
        var errorStage = "setup";
        try
        {
            var currentOptions = (await autoResponseSettings.GetAsync(cancellationToken)).ToOptions();
            if (!currentOptions.Enabled)
            {
                ClearPendingDelay(channelId, evaluationVersion);
                return;
            }

            var delay = TimeSpan.FromSeconds(NormalizeRange(
                currentOptions.IntervalSeconds,
                DefaultIntervalSeconds,
                MaxIntervalSeconds));
            await Task.Delay(delay, cancellationToken);

            currentOptions = (await autoResponseSettings.GetAsync(cancellationToken)).ToOptions();
            if (!currentOptions.Enabled)
            {
                batch = TakeBatch(channelId, evaluationVersion, out _);
                await RecordEventAsync(
                    batch,
                    "disabled",
                    "auto_response_disabled",
                    string.Empty,
                    cancellationToken);
                ClearActiveEvaluation(channelId, evaluationVersion);
                return;
            }

            batch = TakeBatch(channelId, evaluationVersion, out var respondAsync);
            if (batch.Count == 0 || respondAsync == null)
            {
                return;
            }

            if (IsCoolingDown(channelId, currentOptions))
            {
                logger.LogInformation("Skipping auto response for channel {ChannelId}: cooldown is active.", channelId);
                await RecordEventAsync(
                    batch,
                    "cooldown",
                    "cooldown_active",
                    string.Empty,
                    cancellationToken);
                ClearActiveEvaluation(channelId, evaluationVersion);
                return;
            }

            errorStage = "classifier";
            var decision = await evaluator.EvaluateAsync(batch, cancellationToken);
            if (!decision.ShouldRespond)
            {
                logger.LogInformation(
                    "Auto response declined for channel {ChannelId}: {Reason}",
                    channelId,
                    decision.Reason);
                await RecordEventAsync(
                    batch,
                    "declined",
                    decision.Reason,
                    decision.Focus,
                    cancellationToken);
                ClearActiveEvaluation(channelId, evaluationVersion);
                return;
            }

            if (!IsCurrentEvaluation(channelId, evaluationVersion))
            {
                logger.LogInformation(
                    "Skipping stale auto response for channel {ChannelId}: conversation changed during evaluation.",
                    channelId);
                await RecordEventAsync(
                    batch,
                    "stale",
                    "conversation_changed_during_evaluation",
                    decision.Focus,
                    cancellationToken);
                return;
            }

            MarkResponded(channelId, evaluationVersion);
            logger.LogInformation(
                "Auto response accepted for channel {ChannelId}: {Reason}",
                channelId,
                decision.Reason);
            await RecordEventAsync(
                batch,
                "accepted",
                decision.Reason,
                decision.Focus,
                cancellationToken);
            errorStage = "response";
            await respondAsync(new DiscordAutoResponseRequest(batch, decision), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception e)
        {
            if (batch.Count == 0)
            {
                batch = TakeBatch(channelId, evaluationVersion, out _);
            }

            var diagnostic = AutoResponseEventDiagnostic.FromException(errorStage, e);
            LogAutoResponseFailure(channelId, e.GetType().Name, diagnostic);
            await RecordEventAsync(
                batch,
                "error",
                e.GetType().Name,
                string.Empty,
                CancellationToken.None,
                diagnostic);
            ClearActiveEvaluation(channelId, evaluationVersion);
        }
        finally
        {
            delayCts.Dispose();
        }
    }

    private void ClearPendingDelay(string channelId, int evaluationVersion)
    {
        lock (m_Lock)
        {
            if (!m_Channels.TryGetValue(channelId, out var state)
                || state.EvaluationVersion != evaluationVersion)
            {
                return;
            }

            state.DelayCts = null;
            state.Messages.Clear();
            state.RespondAsync = null;
            state.IsEvaluating = false;
        }
    }

    private IReadOnlyList<DiscordAutoResponseMessage> TakeBatch(
        string channelId,
        int evaluationVersion,
        out Func<DiscordAutoResponseRequest, CancellationToken, Task>? respondAsync)
    {
        lock (m_Lock)
        {
            if (!m_Channels.TryGetValue(channelId, out var state)
                || state.EvaluationVersion != evaluationVersion)
            {
                respondAsync = null;
                return [];
            }

            state.DelayCts = null;
            respondAsync = state.RespondAsync;
            state.RespondAsync = null;
            state.IsEvaluating = true;

            var batch = state.Messages
                .OrderBy(message => message.Timestamp)
                .ToArray();
            state.Messages.Clear();
            return batch;
        }
    }

    private bool IsCurrentEvaluation(string channelId, int evaluationVersion)
    {
        lock (m_Lock)
        {
            return m_Channels.TryGetValue(channelId, out var state)
                   && state.EvaluationVersion == evaluationVersion
                   && state.IsEvaluating;
        }
    }

    private void ClearActiveEvaluation(string channelId, int evaluationVersion)
    {
        lock (m_Lock)
        {
            if (!m_Channels.TryGetValue(channelId, out var state)
                || state.EvaluationVersion != evaluationVersion)
            {
                return;
            }

            state.IsEvaluating = false;
        }
    }

    private bool IsCoolingDown(string channelId, AutoResponseOptions options)
    {
        lock (m_Lock)
        {
            if (!m_Channels.TryGetValue(channelId, out var state)
                || state.LastResponseAt == null)
            {
                return false;
            }

            var cooldown = TimeSpan.FromSeconds(NormalizeRange(
                options.CooldownSeconds,
                DefaultCooldownSeconds,
                MaxCooldownSeconds));
            return DateTimeOffset.UtcNow - state.LastResponseAt.Value < cooldown;
        }
    }

    private async ValueTask RecordEventAsync(
        IReadOnlyList<DiscordAutoResponseMessage> batch,
        string decision,
        string reason,
        string focus,
        CancellationToken cancellationToken,
        AutoResponseEventDiagnostic? diagnostic = null)
    {
        if (batch.Count == 0)
        {
            return;
        }

        try
        {
            await autoResponseSettings.RecordEventAsync(
                batch,
                decision,
                reason,
                focus,
                diagnostic,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception e)
        {
            logger.LogWarning(
                "Failed to record Discord auto response event with {ExceptionType}.",
                e.GetType().Name);
        }
    }

    private void LogAutoResponseFailure(
        string channelId,
        string exceptionType,
        AutoResponseEventDiagnostic diagnostic)
    {
        logger.LogError(
            "Discord auto response failed during {Stage} stage for channel {ChannelId} with {ExceptionType} and HTTP status {HttpStatusCode}.",
            diagnostic.Stage,
            channelId,
            exceptionType,
            diagnostic.HttpStatusCode);
    }

    private void MarkResponded(string channelId, int evaluationVersion)
    {
        lock (m_Lock)
        {
            if (!m_Channels.TryGetValue(channelId, out var state))
            {
                state = new ChannelState();
                m_Channels.Add(channelId, state);
            }

            if (state.EvaluationVersion == evaluationVersion)
            {
                state.IsEvaluating = false;
            }

            state.LastResponseAt = DateTimeOffset.UtcNow;
        }
    }

    private static void AddMessage(
        ChannelState state,
        DiscordAutoResponseMessage message,
        AutoResponseOptions options)
    {
        state.Messages.Add(message);

        var maxMessages = NormalizeRange(
            options.MaxBufferedMessages,
            DefaultMaxBufferedMessages,
            MaxBufferedMessagesLimit);
        if (state.Messages.Count <= maxMessages)
        {
            return;
        }

        state.Messages.RemoveRange(0, state.Messages.Count - maxMessages);
    }

    private static int NormalizeRange(int value, int fallback, int max)
    {
        if (value <= 0)
        {
            value = fallback;
        }

        return Math.Clamp(value, 1, max);
    }

    private sealed class ChannelState
    {
        public List<DiscordAutoResponseMessage> Messages { get; } = [];

        public CancellationTokenSource? DelayCts { get; set; }

        public Func<DiscordAutoResponseRequest, CancellationToken, Task>? RespondAsync { get; set; }

        public DateTimeOffset? LastResponseAt { get; set; }

        public int EvaluationVersion { get; set; }

        public bool IsEvaluating { get; set; }
    }
}
