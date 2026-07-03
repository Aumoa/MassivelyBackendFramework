using System.Text;
using System.Text.RegularExpressions;

namespace DiscordBot.Services;

public sealed record AiSkillDefinition(
    string Name,
    string Description,
    int Priority,
    IReadOnlyList<string> TriggerPhrases,
    string Instructions);

public interface IAiSkillProvider
{
    IReadOnlyList<AiSkillDefinition> SelectSkills(string prompt);
}

internal sealed partial class FileAiSkillProvider(
    IHostEnvironment environment,
    ILogger<FileAiSkillProvider> logger) : IAiSkillProvider
{
    private const string SkillsDirectoryName = "AiSkills";
    private const int MaxSelectedSkills = 3;

    public IReadOnlyList<AiSkillDefinition> SelectSkills(string prompt)
    {
        var selectionText = ExtractSelectionText(prompt);
        if (string.IsNullOrWhiteSpace(selectionText))
        {
            return [];
        }

        var requestedSkillNames = ExtractRequestedSkillNames(selectionText);
        var skills = LoadSkills();

        return skills
            .Where(skill => requestedSkillNames.Contains(skill.Name) || MatchesTriggerPhrase(selectionText, skill))
            .OrderByDescending(skill => skill.Priority)
            .ThenBy(skill => skill.Name, StringComparer.Ordinal)
            .Take(MaxSelectedSkills)
            .ToArray();
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

    internal static AiSkillDefinition? ParseSkill(string content, string sourceName)
    {
        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        if (!normalized.StartsWith("---\n", StringComparison.Ordinal))
        {
            throw new FormatException($"AI Skill '{sourceName}' must start with frontmatter.");
        }

        var endIndex = normalized.IndexOf("\n---\n", 4, StringComparison.Ordinal);
        if (endIndex < 0)
        {
            throw new FormatException($"AI Skill '{sourceName}' frontmatter was not closed.");
        }

        var frontmatter = normalized[4..endIndex];
        var instructions = normalized[(endIndex + "\n---\n".Length)..].Trim();
        if (string.IsNullOrWhiteSpace(instructions))
        {
            throw new FormatException($"AI Skill '{sourceName}' must include instruction body content.");
        }

        var values = ParseFrontmatter(frontmatter);
        var name = GetRequiredValue(values, "name", sourceName);
        if (!SkillNameRegex().IsMatch(name))
        {
            throw new FormatException($"AI Skill '{sourceName}' has an invalid name '{name}'.");
        }

        var description = GetRequiredValue(values, "description", sourceName);
        var priority = 0;
        if (values.TryGetValue("priority", out var priorityValues)
            && priorityValues.Count > 0
            && !int.TryParse(priorityValues[0], out priority))
        {
            throw new FormatException($"AI Skill '{sourceName}' has an invalid priority.");
        }

        var triggerPhrases = values.TryGetValue("trigger_phrases", out var phrases)
            ? phrases.Where(phrase => !string.IsNullOrWhiteSpace(phrase)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            : [];

        if (triggerPhrases.Length == 0)
        {
            throw new FormatException($"AI Skill '{sourceName}' must include at least one trigger phrase.");
        }

        return new AiSkillDefinition(name, description, priority, triggerPhrases, instructions);
    }

    private IReadOnlyList<AiSkillDefinition> LoadSkills()
    {
        var skillsDirectory = Path.Combine(environment.ContentRootPath, SkillsDirectoryName);
        if (!Directory.Exists(skillsDirectory))
        {
            logger.LogDebug("AI Skill directory was not found: {Path}", skillsDirectory);
            return [];
        }

        var skills = new List<AiSkillDefinition>();
        foreach (var path in Directory.EnumerateFiles(skillsDirectory, "*.md", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var content = File.ReadAllText(path);
                var skill = ParseSkill(content, Path.GetFileName(path));
                if (skill != null)
                {
                    skills.Add(skill);
                }
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Failed to load AI Skill: {Path}", path);
            }
        }

        return skills;
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
            throw new FormatException($"AI Skill '{sourceName}' must include '{key}'.");
        }

        return keyValues[0].Trim();
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
}
