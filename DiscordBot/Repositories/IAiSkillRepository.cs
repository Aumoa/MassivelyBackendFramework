namespace DiscordBot.Repositories;

public sealed record AiSkillData(
    string Name,
    string Description,
    int Priority,
    string TriggerPhrasesJson,
    string Instructions,
    bool Enabled,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public interface IAiSkillRepository
{
    ValueTask<IReadOnlyList<AiSkillData>> GetAllAsync(CancellationToken cancellationToken = default);

    ValueTask UpsertAsync(
        string name,
        string description,
        int priority,
        IReadOnlyList<string> triggerPhrases,
        string instructions,
        bool enabled,
        CancellationToken cancellationToken = default);
}
