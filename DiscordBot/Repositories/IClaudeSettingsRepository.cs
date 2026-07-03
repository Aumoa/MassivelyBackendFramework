namespace DiscordBot.Repositories;

public record ClaudeSettingsData(
    string Model,
    string SummaryModel,
    int DefaultMaxTokens,
    string? Instructions,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public interface IClaudeSettingsRepository
{
    ValueTask<ClaudeSettingsData?> GetAsync(CancellationToken cancellationToken = default);

    ValueTask UpsertAsync(
        string model,
        string summaryModel,
        int defaultMaxTokens,
        string instructions,
        CancellationToken cancellationToken = default);
}
