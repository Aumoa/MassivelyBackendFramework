using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DiscordBot.Options;
using DiscordBot.Repositories;
using Microsoft.Extensions.Options;

namespace DiscordBot.Services;

public enum AiSkillSource
{
    Database,
    Local
}

public sealed record AiSkillDefinition(
    string Name,
    string Description,
    int Priority,
    IReadOnlyList<string> TriggerPhrases,
    string Instructions,
    AiSkillSource Source,
    IReadOnlyList<string> ToolNames)
{
    public AiSkillDefinition(
        string name,
        string description,
        int priority,
        IReadOnlyList<string> triggerPhrases,
        string instructions)
        : this(name, description, priority, triggerPhrases, instructions, AiSkillSource.Database, [])
    {
    }
}

public sealed record AiSkillSelection(
    IReadOnlyList<AiSkillDefinition> Skills,
    IReadOnlySet<string> ToolNames);

public interface IAiSkillProvider
{
    ValueTask<IReadOnlyList<AiSkillDefinition>> GetActiveSkillsAsync(CancellationToken cancellationToken = default);

    ValueTask<AiSkillSelection> SelectSkillsAsync(
        string prompt,
        CancellationToken cancellationToken = default);
}

internal interface IAiSkillTemplateProvider
{
    IReadOnlyList<AiSkillDefinition> LoadTemplates();
}

internal sealed partial class AiSkillProvider(
    IAiSkillRepository repository,
    IAiSkillTemplateProvider templateProvider,
    ILogger<AiSkillProvider> logger) : IAiSkillProvider
{
    private const int MaxSelectedSkills = 3;

    public async ValueTask<IReadOnlyList<AiSkillDefinition>> GetActiveSkillsAsync(CancellationToken cancellationToken = default)
    {
        var activeSkills = await LoadAndSeedSkillsAsync(cancellationToken);
        activeSkills = activeSkills
            .OrderByDescending(skill => skill.Priority)
            .ThenBy(skill => skill.Name, StringComparer.Ordinal)
            .ToArray();

        return activeSkills;
    }

    public async ValueTask<AiSkillSelection> SelectSkillsAsync(
        string prompt,
        CancellationToken cancellationToken = default)
    {
        var skills = await GetActiveSkillsAsync(cancellationToken);
        var selectionText = ExtractSelectionText(prompt);
        if (string.IsNullOrWhiteSpace(selectionText))
        {
            return new AiSkillSelection([], new HashSet<string>(StringComparer.Ordinal));
        }

        var requestedSkillNames = ExtractRequestedSkillNames(selectionText);

        var matchedSkills = skills
            .Where(skill => requestedSkillNames.Contains(skill.Name) || MatchesTriggerPhrase(selectionText, skill))
            .OrderByDescending(skill => skill.Priority)
            .ThenBy(skill => skill.Name, StringComparer.Ordinal)
            .ToArray();

        var selectedSkills = matchedSkills
            .Take(MaxSelectedSkills)
            .ToArray();

        var selectedToolNames = matchedSkills
            .Where(skill => skill.Source == AiSkillSource.Local)
            .SelectMany(skill => skill.ToolNames)
            .Where(toolName => !string.IsNullOrWhiteSpace(toolName))
            .ToHashSet(StringComparer.Ordinal);

        return new AiSkillSelection(selectedSkills, selectedToolNames);
    }

    public static string BuildSystemInstruction(IReadOnlyList<AiSkillDefinition> skills)
    {
        if (skills.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        sb.AppendLine("[요청 기반 추가 Skill 지침]");
        sb.AppendLine("- 이 지침은 사용자가 해당 답변 방식을 요청한 현재 응답에만 적용합니다.");
        sb.AppendLine("- 전역 AI 지침, 기본 응답 방침, 도구 권한, 채널 범위, 안전성, 사실성 지침보다 낮은 우선순위입니다.");
        sb.AppendLine("- DB에서 관리되는 Skill 지침은 tool 권한을 추가하지 않습니다. tool 노출은 로컬 source-of-truth Skill의 허용 목록과 서버 설정만 따릅니다.");
        sb.AppendLine("- 여러 Skill이 함께 선택되면 서로 충돌하지 않는 범위에서만 적용하고, 충돌 시 더 구체적인 사용자 요청과 상위 지침을 우선합니다.");

        foreach (var skill in skills)
        {
            sb.AppendLine();
            sb.AppendLine($"[Skill: {skill.Name}]");
            sb.AppendLine($"Description: {skill.Description}");
            sb.AppendLine(skill.Instructions.Trim());
        }

        return sb.ToString();
    }

    internal static string ExtractSelectionText(string prompt)
    {
        const string USER_MESSAGE_MARKER = "[사용자 메시지]";
        var userMessageIndex = prompt.LastIndexOf(USER_MESSAGE_MARKER, StringComparison.Ordinal);
        if (userMessageIndex >= 0)
        {
            return prompt[(userMessageIndex + USER_MESSAGE_MARKER.Length)..].Trim();
        }

        const string ATTACHMENT_MARKER = "\n\n[첨부 문서]";
        var attachmentIndex = prompt.IndexOf(ATTACHMENT_MARKER, StringComparison.Ordinal);
        if (attachmentIndex >= 0)
        {
            return prompt[..attachmentIndex].Trim();
        }

        return prompt.Trim();
    }

    internal static AiSkillDefinition ParseSkillTemplate(string content, string sourceName)
    {
        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        if (!normalized.StartsWith("---\n", StringComparison.Ordinal))
        {
            throw new FormatException($"AI Skill template '{sourceName}' must start with frontmatter.");
        }

        var endIndex = normalized.IndexOf("\n---\n", 4, StringComparison.Ordinal);
        if (endIndex < 0)
        {
            throw new FormatException($"AI Skill template '{sourceName}' frontmatter was not closed.");
        }

        var frontmatter = normalized[4..endIndex];
        var instructions = normalized[(endIndex + "\n---\n".Length)..].Trim();
        if (string.IsNullOrWhiteSpace(instructions))
        {
            throw new FormatException($"AI Skill template '{sourceName}' must include instruction body content.");
        }

        var values = ParseFrontmatter(frontmatter);
        var name = GetRequiredValue(values, "name", sourceName);
        if (!SkillNameRegex().IsMatch(name))
        {
            throw new FormatException($"AI Skill template '{sourceName}' has an invalid name '{name}'.");
        }

        var description = GetRequiredValue(values, "description", sourceName);
        var source = AiSkillSource.Database;
        if (values.TryGetValue("source", out var sourceValues)
            && sourceValues.Count > 0
            && !string.IsNullOrWhiteSpace(sourceValues[0]))
        {
            source = ParseSource(sourceValues[0], sourceName);
        }

        var priority = 0;
        if (values.TryGetValue("priority", out var priorityValues)
            && priorityValues.Count > 0
            && !int.TryParse(priorityValues[0], out priority))
        {
            throw new FormatException($"AI Skill template '{sourceName}' has an invalid priority.");
        }

        var triggerPhrases = values.TryGetValue("trigger_phrases", out var phrases)
            ? phrases.Where(phrase => !string.IsNullOrWhiteSpace(phrase)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            : [];

        if (triggerPhrases.Length == 0)
        {
            throw new FormatException($"AI Skill template '{sourceName}' must include at least one trigger phrase.");
        }

        var toolNames = values.TryGetValue("tool_names", out var toolNameValues)
            ? toolNameValues
                .Where(toolName => !string.IsNullOrWhiteSpace(toolName))
                .Select(toolName => toolName.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray()
            : [];

        foreach (var toolName in toolNames)
        {
            if (!ToolNameRegex().IsMatch(toolName))
            {
                throw new FormatException($"AI Skill template '{sourceName}' has an invalid tool name '{toolName}'.");
            }
        }

        if (source == AiSkillSource.Database && toolNames.Length > 0)
        {
            throw new FormatException($"AI Skill template '{sourceName}' cannot define tool_names unless source is local.");
        }

        return new AiSkillDefinition(name, description, priority, triggerPhrases, instructions, source, toolNames);
    }

    private async ValueTask<IReadOnlyList<AiSkillDefinition>> LoadAndSeedSkillsAsync(CancellationToken cancellationToken)
    {
        var templates = templateProvider.LoadTemplates();
        var localTemplates = templates
            .Where(template => template.Source == AiSkillSource.Local)
            .ToArray();
        var localSkillNames = localTemplates
            .Select(template => template.Name)
            .ToHashSet(StringComparer.Ordinal);
        var databaseTemplates = templates
            .Where(template => template.Source == AiSkillSource.Database)
            .Where(template => !localSkillNames.Contains(template.Name))
            .ToArray();

        var skills = await repository.GetAllAsync(cancellationToken);
        var existingSkillNames = skills.Select(skill => skill.Name).ToHashSet(StringComparer.Ordinal);
        var seededAnySkill = false;

        foreach (var template in databaseTemplates)
        {
            if (existingSkillNames.Contains(template.Name))
            {
                continue;
            }

            await repository.UpsertAsync(
                template.Name,
                template.Description,
                template.Priority,
                template.TriggerPhrases,
                template.Instructions,
                enabled: true,
                cancellationToken);
            existingSkillNames.Add(template.Name);
            seededAnySkill = true;
        }

        if (seededAnySkill)
        {
            skills = await repository.GetAllAsync(cancellationToken);
        }

        var databaseSkills = skills
            .Where(skill => skill.Enabled)
            .Where(skill => !localSkillNames.Contains(skill.Name))
            .Select(ToDefinition)
            .OfType<AiSkillDefinition>();

        return databaseSkills.Concat(localTemplates).ToArray();
    }

    private AiSkillDefinition? ToDefinition(AiSkillData data)
    {
        try
        {
            var triggerPhrases = JsonSerializer.Deserialize<string[]>(data.TriggerPhrasesJson) ?? [];
            return new AiSkillDefinition(
                data.Name,
                data.Description,
                data.Priority,
                triggerPhrases,
                data.Instructions,
                AiSkillSource.Database,
                []);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Failed to parse AI Skill trigger phrases from database: {SkillName}", data.Name);
            return null;
        }
    }

    private static bool MatchesTriggerPhrase(string selectionText, AiSkillDefinition skill)
    {
        return skill.TriggerPhrases.Any(phrase =>
            selectionText.Contains(phrase, StringComparison.OrdinalIgnoreCase));
    }

    private static HashSet<string> ExtractRequestedSkillNames(string selectionText)
    {
        var requestedSkillNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in RequestedSkillRegex().Matches(selectionText))
        {
            requestedSkillNames.Add(match.Groups["name"].Value);
        }

        return requestedSkillNames;
    }

    private static Dictionary<string, List<string>> ParseFrontmatter(string frontmatter)
    {
        var values = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        string? currentListKey = null;

        foreach (var rawLine in frontmatter.Split('\n'))
        {
            var line = rawLine.TrimEnd();
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#'))
            {
                continue;
            }

            var trimmed = line.Trim();
            if (currentListKey != null && trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                values[currentListKey].Add(Unquote(trimmed[2..].Trim()));
                continue;
            }

            var separatorIndex = trimmed.IndexOf(':');
            if (separatorIndex <= 0)
            {
                currentListKey = null;
                continue;
            }

            var key = trimmed[..separatorIndex].Trim();
            var value = trimmed[(separatorIndex + 1)..].Trim();
            if (!values.TryGetValue(key, out var keyValues))
            {
                keyValues = [];
                values.Add(key, keyValues);
            }

            if (string.IsNullOrEmpty(value))
            {
                currentListKey = key;
            }
            else
            {
                keyValues.Add(Unquote(value));
                currentListKey = null;
            }
        }

        return values;
    }

    private static string GetRequiredValue(Dictionary<string, List<string>> values, string key, string sourceName)
    {
        if (!values.TryGetValue(key, out var keyValues)
            || keyValues.Count == 0
            || string.IsNullOrWhiteSpace(keyValues[0]))
        {
            throw new FormatException($"AI Skill template '{sourceName}' must include '{key}'.");
        }

        return keyValues[0].Trim();
    }

    private static AiSkillSource ParseSource(string value, string sourceName)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "database" or "db" => AiSkillSource.Database,
            "local" or "file" or "static" or "appsettings" => AiSkillSource.Local,
            _ => throw new FormatException($"AI Skill template '{sourceName}' has an invalid source '{value}'.")
        };
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2
            && ((value[0] == '"' && value[^1] == '"')
                || (value[0] == '\'' && value[^1] == '\'')))
        {
            return value[1..^1];
        }

        return value;
    }

    [GeneratedRegex(@"(?:\$|skill:)(?<name>[a-z0-9][a-z0-9-]{0,63})", RegexOptions.CultureInvariant)]
    private static partial Regex RequestedSkillRegex();

    [GeneratedRegex(@"^[a-z0-9][a-z0-9-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex SkillNameRegex();

    [GeneratedRegex(@"^[a-z0-9][a-z0-9_]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex ToolNameRegex();
}

internal sealed class FileAiSkillTemplateProvider(
    IOptions<AiSkillOptions> options,
    IHostEnvironment environment,
    ILogger<FileAiSkillTemplateProvider> logger) : IAiSkillTemplateProvider
{
    private readonly AiSkillOptions m_Options = options.Value;
    private readonly object m_CacheLock = new();
    private IReadOnlyList<AiSkillDefinition>? m_CachedTemplates;

    public IReadOnlyList<AiSkillDefinition> LoadTemplates()
    {
        if (m_CachedTemplates != null)
        {
            return m_CachedTemplates;
        }

        lock (m_CacheLock)
        {
            m_CachedTemplates ??= LoadTemplatesCore();
            return m_CachedTemplates;
        }
    }

    private IReadOnlyList<AiSkillDefinition> LoadTemplatesCore()
    {
        var templateDirectoryPath = m_Options.TemplateDirectoryPath;
        if (!Path.IsPathRooted(templateDirectoryPath))
        {
            templateDirectoryPath = Path.Combine(environment.ContentRootPath, templateDirectoryPath);
        }

        if (!Directory.Exists(templateDirectoryPath))
        {
            logger.LogDebug("AI Skill template directory was not found: {Path}", templateDirectoryPath);
            return [];
        }

        var templates = new List<AiSkillDefinition>();
        foreach (var path in Directory.EnumerateFiles(templateDirectoryPath, "*.md", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var content = File.ReadAllText(path);
                templates.Add(AiSkillProvider.ParseSkillTemplate(content, Path.GetFileName(path)));
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Failed to load AI Skill template: {Path}", path);
            }
        }

        return templates;
    }
}
