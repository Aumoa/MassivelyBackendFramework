namespace DiscordBot.Repositories;

public sealed record AmbientChatContextSettingsData(
    bool Enabled,
    int WindowMessageCount,
    int WindowMaxChars,
    int LookbackMinutes,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public interface IAmbientChatContextSettingsRepository
{
    ValueTask<AmbientChatContextSettingsData?> GetAsync(CancellationToken cancellationToken = default);

    ValueTask UpsertAsync(
        bool enabled,
        int windowMessageCount,
        int windowMaxChars,
        int lookbackMinutes,
        CancellationToken cancellationToken = default);
}
