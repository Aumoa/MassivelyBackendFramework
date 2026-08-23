namespace DiscordBot.Repositories;

public sealed record DailyImagePostSettingsData(
    bool Enabled,
    string ChannelId,
    string PostTimeOfDay,
    string ThemePrompt,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public interface IDailyImagePostSettingsRepository
{
    ValueTask<DailyImagePostSettingsData?> GetAsync(CancellationToken cancellationToken = default);

    ValueTask UpsertAsync(
        bool enabled,
        string channelId,
        string postTimeOfDay,
        string themePrompt,
        CancellationToken cancellationToken = default);
}
