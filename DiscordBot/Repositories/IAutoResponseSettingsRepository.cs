namespace DiscordBot.Repositories;

public sealed record AutoResponseSettingsData(
    bool Enabled,
    int IntervalSeconds,
    int CooldownSeconds,
    int MaxBufferedMessages,
    int ClassifierMaxTokens,
    string? ClassifierModel,
    string BotNameAliasesJson,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public sealed record AutoResponseEventData(
    long Id,
    string? GuildId,
    string ChannelId,
    string? TriggerMessageId,
    string MessageIdsJson,
    string Decision,
    string Reason,
    string Focus,
    string ErrorStage,
    int? HttpStatusCode,
    string ErrorMessage,
    DateTime CreatedAt);

public sealed record AutoResponseEventInput(
    string? GuildId,
    string ChannelId,
    string? TriggerMessageId,
    string MessageIdsJson,
    string Decision,
    string Reason,
    string Focus,
    string ErrorStage,
    int? HttpStatusCode,
    string ErrorMessage);

public interface IAutoResponseSettingsRepository
{
    ValueTask<AutoResponseSettingsData?> GetAsync(CancellationToken cancellationToken = default);

    ValueTask UpsertAsync(
        bool enabled,
        int intervalSeconds,
        int cooldownSeconds,
        int maxBufferedMessages,
        int classifierMaxTokens,
        string? classifierModel,
        string botNameAliasesJson,
        CancellationToken cancellationToken = default);
}

public interface IAutoResponseEventRepository
{
    ValueTask AddAsync(AutoResponseEventInput input, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<AutoResponseEventData>> GetRecentAsync(
        int limit,
        CancellationToken cancellationToken = default);
}
