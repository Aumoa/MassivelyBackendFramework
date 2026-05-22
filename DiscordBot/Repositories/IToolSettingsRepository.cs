namespace DiscordBot.Repositories;

public record ToolSettingData(
    string ToolName,
    bool Enabled,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public interface IToolSettingsRepository
{
    ValueTask<IReadOnlyList<ToolSettingData>> GetAllAsync(CancellationToken cancellationToken = default);

    ValueTask<ToolSettingData?> GetAsync(string toolName, CancellationToken cancellationToken = default);

    ValueTask UpsertAsync(string toolName, bool enabled, CancellationToken cancellationToken = default);
}
