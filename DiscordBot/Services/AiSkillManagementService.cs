using System.Text.Json;
using System.Text.RegularExpressions;
using DiscordBot.Repositories;

namespace DiscordBot.Services;

internal sealed record AiSkillManagementView(
    string Name,
    string Description,
    int Priority,
    IReadOnlyList<string> TriggerPhrases,
    string Instructions,
    bool Enabled,
    AiSkillSource Source,
    IReadOnlyList<string> ToolNames,
    DateTime? CreatedAt,
    DateTime? UpdatedAt)
{
    public bool IsEditable => Source == AiSkillSource.Database;
}

internal sealed record AiSkillSaveRequest(
    string Name,
    string Description,
    int Priority,
    IReadOnlyList<string> TriggerPhrases,
    string Instructions,
    bool Enabled);

internal sealed record AiConfigurationSearchResult(
    string Kind,
    string Name,
    AiSkillSource? Source,
    string Snippet);

internal interface IAiSkillManagementService
{
    ValueTask<IReadOnlyList<AiSkillManagementView>> GetAllAsync(CancellationToken cancellationToken = default);

    ValueTask<AiSkillManagementView?> GetAsync(string name, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<AiConfigurationSearchResult>> SearchAsync(
        string query,
        int maxResults = 10,
        CancellationToken cancellationToken = default);

    ValueTask SaveDatabaseSkillAsync(AiSkillSaveRequest request, CancellationToken cancellationToken = default);

    ValueTask RenameDatabaseSkillAsync(
        string name,
        string newName,
        CancellationToken cancellationToken = default);

    ValueTask DeleteDatabaseSkillAsync(string name, CancellationToken cancellationToken = default);
}

internal sealed partial class AiSkillManagementService(
    IAiSkillRepository repository,
    IAiSkillTemplateProvider templateProvider,
    IClaudeSettingsService claudeSettings) : IAiSkillManagementService
{
    private const int MaxDescriptionLength = 1024;
    private const int MaxInstructionsLength = 262144;
    private const int MaxTriggerPhraseLength = 256;

    public async ValueTask<IReadOnlyList<AiSkillManagementView>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var localSkills = LoadLocalSkills();
        var localNames = localSkills
            .Select(skill => skill.Name)
            .ToHashSet(StringComparer.Ordinal);
        var databaseSkills = await repository.GetAllAsync(cancellationToken);

        return databaseSkills
            .Where(skill => !localNames.Contains(skill.Name))
            .Select(ToView)
            .Concat(localSkills)
            .OrderByDescending(skill => skill.Priority)
            .ThenBy(skill => skill.Name, StringComparer.Ordinal)
            .ToArray();
    }

    public async ValueTask<AiSkillManagementView?> GetAsync(string name, CancellationToken cancellationToken = default)
    {
        var normalizedName = NormalizeName(name, nameof(name));
        var localSkill = LoadLocalSkills().FirstOrDefault(skill => skill.Name == normalizedName);
        if (localSkill != null)
        {
            return localSkill;
        }

        var databaseSkill = await repository.GetAsync(normalizedName, cancellationToken);
        return databaseSkill == null ? null : ToView(databaseSkill);
    }

    public async ValueTask<IReadOnlyList<AiConfigurationSearchResult>> SearchAsync(
        string query,
        int maxResults = 10,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuery = query?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedQuery))
        {
            return [];
        }

        maxResults = Math.Clamp(maxResults, 1, 50);
        var results = new List<AiConfigurationSearchResult>();
        var settings = await claudeSettings.GetAsync(cancellationToken);
        AddIfMatches(
            results,
            "instructions",
            "global",
            null,
            settings.Instructions ?? string.Empty,
            normalizedQuery);

        var skills = await GetAllAsync(cancellationToken);
        foreach (var skill in skills)
        {
            var searchable = string.Join('\n',
                skill.Name,
                skill.Description,
                string.Join('\n', skill.TriggerPhrases),
                skill.Instructions,
                string.Join('\n', skill.ToolNames));

            AddIfMatches(
                results,
                "skill",
                skill.Name,
                skill.Source,
                searchable,
                normalizedQuery);

            if (results.Count >= maxResults)
            {
                break;
            }
        }

        return results.Take(maxResults).ToArray();
    }

    public async ValueTask SaveDatabaseSkillAsync(AiSkillSaveRequest request, CancellationToken cancellationToken = default)
    {
        var name = NormalizeName(request.Name, nameof(request.Name));
        EnsureNotLocalSkill(name);
        var description = NormalizeDescription(request.Description);
        var triggerPhrases = NormalizeTriggerPhrases(request.TriggerPhrases);
        var instructions = NormalizeInstructions(request.Instructions);

        await repository.UpsertAsync(
            name,
            description,
            request.Priority,
            triggerPhrases,
            instructions,
            request.Enabled,
            cancellationToken);
    }

    public async ValueTask RenameDatabaseSkillAsync(
        string name,
        string newName,
        CancellationToken cancellationToken = default)
    {
        var normalizedName = NormalizeName(name, nameof(name));
        var normalizedNewName = NormalizeName(newName, nameof(newName));
        EnsureNotLocalSkill(normalizedName);
        EnsureNotLocalSkill(normalizedNewName);

        var existing = await repository.GetAsync(normalizedName, cancellationToken);
        if (existing == null)
        {
            throw new InvalidOperationException("AI Skill was not found.");
        }

        if (!string.Equals(normalizedName, normalizedNewName, StringComparison.Ordinal)
            && await GetAsync(normalizedNewName, cancellationToken) != null)
        {
            throw new InvalidOperationException("An AI Skill with the new name already exists.");
        }

        await repository.RenameAsync(normalizedName, normalizedNewName, cancellationToken);
    }

    public async ValueTask DeleteDatabaseSkillAsync(string name, CancellationToken cancellationToken = default)
    {
        var normalizedName = NormalizeName(name, nameof(name));
        EnsureNotLocalSkill(normalizedName);
        await repository.DeleteAsync(normalizedName, cancellationToken);
    }

    private IReadOnlyList<AiSkillManagementView> LoadLocalSkills()
    {
        return templateProvider.LoadTemplates()
            .Where(skill => skill.Source == AiSkillSource.Local)
            .Select(skill => new AiSkillManagementView(
                skill.Name,
                skill.Description,
                skill.Priority,
                skill.TriggerPhrases,
                skill.Instructions,
                Enabled: true,
                skill.Source,
                skill.ToolNames,
                CreatedAt: null,
                UpdatedAt: null))
            .ToArray();
    }

    private AiSkillManagementView ToView(AiSkillData data)
    {
        var triggerPhrases = JsonSerializer.Deserialize<string[]>(data.TriggerPhrasesJson) ?? [];
        return new AiSkillManagementView(
            data.Name,
            data.Description,
            data.Priority,
            triggerPhrases,
            data.Instructions,
            data.Enabled,
            AiSkillSource.Database,
            [],
            data.CreatedAt,
            data.UpdatedAt);
    }

    private static void AddIfMatches(
        List<AiConfigurationSearchResult> results,
        string kind,
        string name,
        AiSkillSource? source,
        string content,
        string query)
    {
        var index = content.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return;
        }

        var start = Math.Max(0, index - 80);
        var length = Math.Min(content.Length - start, query.Length + 160);
        var snippet = content[start..(start + length)]
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();

        results.Add(new AiConfigurationSearchResult(kind, name, source, snippet));
    }

    private void EnsureNotLocalSkill(string name)
    {
        if (templateProvider.LoadTemplates().Any(skill => skill.Source == AiSkillSource.Local && skill.Name == name))
        {
            throw new InvalidOperationException("Local source-of-truth AI Skills cannot be modified at runtime.");
        }
    }

    internal static string NormalizeName(string value, string parameterName)
    {
        var normalized = Uri.UnescapeDataString(value ?? string.Empty).Trim();
        if (!SkillNameRegex().IsMatch(normalized))
        {
            throw new ArgumentException("AI Skill name must be 1-64 lowercase letters, numbers, or hyphens.", parameterName);
        }

        return normalized;
    }

    private static string NormalizeDescription(string value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("Description is required.", nameof(value));
        }

        if (normalized.Length > MaxDescriptionLength)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Description is too long.");
        }

        return normalized;
    }

    private static string NormalizeInstructions(string value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length > MaxInstructionsLength)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Instructions are too long.");
        }

        return normalized;
    }

    internal static IReadOnlyList<string> NormalizeTriggerPhrases(IReadOnlyList<string> triggerPhrases)
    {
        var normalized = triggerPhrases
            .Select(phrase => phrase.Trim())
            .Where(phrase => !string.IsNullOrWhiteSpace(phrase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalized.Length == 0)
        {
            throw new ArgumentException("At least one trigger phrase is required.", nameof(triggerPhrases));
        }

        if (normalized.Any(phrase => phrase.Length > MaxTriggerPhraseLength))
        {
            throw new ArgumentOutOfRangeException(nameof(triggerPhrases), triggerPhrases, "Trigger phrase is too long.");
        }

        return normalized;
    }

    [GeneratedRegex(@"^[a-z0-9][a-z0-9-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex SkillNameRegex();
}
