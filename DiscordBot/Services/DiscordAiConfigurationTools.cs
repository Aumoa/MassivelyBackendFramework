using System.Text;
using AI;
using Discord.WebSocket;
using DiscordBot.Options;
using Microsoft.Extensions.Options;

namespace DiscordBot.Services;

internal sealed class DiscordAiConfigurationTools(
    SocketMessage message,
    IClaudeSettingsService claudeSettings,
    IAiSkillManagementService skillManagement,
    IOptions<AiConfigurationManagementOptions> options,
    ILogger<DiscordAiConfigurationTools> logger) : IToolFunctionDescriptionProvider
{
    private const int MaxReturnedCharacters = 16000;

    [ToolFunction(
        Name = "read_ai_instructions",
        Description = """
현재 DB에 저장된 전역 AI 지침을 읽습니다. 지침 수정 요청을 처리하기 전에 먼저 호출하세요. 긴 지침은 offset과 max_characters로 나누어 읽을 수 있습니다.
이 도구는 AI 설정 변경 권한이 있는 Discord 사용자에게만 결과를 반환합니다.
""")]
    public async Task<string> ReadAiInstructionsAsync(
        [ToolParameterInfo(Description = "읽기 시작 위치. 기본값 0.")]
        int offset = 0,
        [ToolParameterInfo(Description = "반환할 최대 글자 수. 1-16000 사이로 제한됩니다.")]
        int max_characters = MaxReturnedCharacters,
        CancellationToken cancellationToken = default)
    {
        if (!IsAuthorized(out var error))
        {
            return error;
        }

        var settings = await claudeSettings.GetAsync(cancellationToken);
        return SliceForTool(settings.Instructions ?? string.Empty, offset, max_characters);
    }

    [ToolFunction(
        Name = "save_ai_instructions",
        Description = """
DB에 저장된 전역 AI 지침을 교체합니다. 저장 전에는 read_ai_instructions로 현재 지침을 읽고, 사용자의 요구를 반영한 전체 지침을 작성해 호출하세요.
부분 패치가 아니라 전체 지침 본문을 저장합니다. 긴 지침은 read_ai_instructions로 모든 조각을 읽은 뒤 전체 본문을 구성하세요.
이 도구는 AI 설정 변경 권한이 있는 Discord 사용자에게만 동작합니다.
""")]
    public async Task<string> SaveAiInstructionsAsync(
        [ToolParameterInfo(Description = "새 전역 AI 지침 전체 본문.")]
        string instructions,
        CancellationToken cancellationToken = default)
    {
        if (!IsAuthorized(out var error))
        {
            return error;
        }

        var settings = await claudeSettings.GetAsync(cancellationToken);
        await claudeSettings.SaveAsync(
            settings.Model,
            settings.SummaryModel,
            settings.DefaultMaxTokens,
            instructions,
            cancellationToken);

        logger.LogInformation(
            "AI instructions updated by Discord user {UserId} via tool.",
            message.Author.Id);
        return "전역 AI 지침을 저장했습니다.";
    }

    [ToolFunction(
        Name = "list_ai_skills",
        Description = """
현재 활성/비활성 DB Skill과 로컬 source-of-truth Skill 목록을 읽습니다. 로컬 Skill은 런타임에 수정할 수 없고, DB Skill만 수정할 수 있습니다.
이 도구는 AI 설정 변경 권한이 있는 Discord 사용자에게만 결과를 반환합니다.
""")]
    public async Task<string> ListAiSkillsAsync(
        [ToolParameterInfo(Description = "결과 최대 개수. 1-100 사이로 제한됩니다.")]
        int max_results = 50,
        CancellationToken cancellationToken = default)
    {
        if (!IsAuthorized(out var error))
        {
            return error;
        }

        max_results = Math.Clamp(max_results, 1, 100);
        var skills = await skillManagement.GetAllAsync(cancellationToken);
        var sb = new StringBuilder();
        foreach (var skill in skills.Take(max_results))
        {
            sb.AppendLine($"- {skill.Name} [{skill.Source}] enabled={skill.Enabled}, priority={skill.Priority}");
            sb.AppendLine($"  description: {skill.Description}");
            sb.AppendLine($"  triggers: {string.Join(", ", skill.TriggerPhrases)}");
            if (skill.ToolNames.Count > 0)
            {
                sb.AppendLine($"  tools: {string.Join(", ", skill.ToolNames)}");
            }
        }

        return TrimForTool(sb.ToString());
    }

    [ToolFunction(
        Name = "read_ai_skill",
        Description = """
지정한 AI Skill의 설명, trigger phrases, 지침 본문을 읽습니다. DB Skill과 로컬 Skill 모두 읽을 수 있지만 수정은 DB Skill만 가능합니다.
Skill 수정 요청을 처리하기 전에 먼저 호출하세요. 이 도구는 AI 설정 변경 권한이 있는 Discord 사용자에게만 결과를 반환합니다.
""")]
    public async Task<string> ReadAiSkillAsync(
        [ToolParameterInfo(Description = "읽을 AI Skill 이름.")]
        string name,
        [ToolParameterInfo(Description = "instructions 본문 읽기 시작 위치. 기본값 0.")]
        int offset = 0,
        [ToolParameterInfo(Description = "instructions 본문에서 반환할 최대 글자 수. 1-16000 사이로 제한됩니다.")]
        int max_characters = MaxReturnedCharacters,
        CancellationToken cancellationToken = default)
    {
        if (!IsAuthorized(out var error))
        {
            return error;
        }

        var skill = await skillManagement.GetAsync(name, cancellationToken);
        if (skill == null)
        {
            return "AI Skill을 찾을 수 없습니다.";
        }

        return FormatSkill(skill, offset, max_characters);
    }

    [ToolFunction(
        Name = "search_ai_configuration",
        Description = """
전역 AI 지침과 AI Skill 이름/설명/trigger/instructions/tool 목록을 키워드로 검색합니다. 수정할 위치를 찾을 때 먼저 사용하세요.
이 도구는 AI 설정 변경 권한이 있는 Discord 사용자에게만 결과를 반환합니다.
""")]
    public async Task<string> SearchAiConfigurationAsync(
        [ToolParameterInfo(Description = "검색할 키워드.")]
        string query,
        [ToolParameterInfo(Description = "결과 최대 개수. 1-50 사이로 제한됩니다.")]
        int max_results = 10,
        CancellationToken cancellationToken = default)
    {
        if (!IsAuthorized(out var error))
        {
            return error;
        }

        var results = await skillManagement.SearchAsync(query, max_results, cancellationToken);
        if (results.Count == 0)
        {
            return "검색 결과가 없습니다.";
        }

        var sb = new StringBuilder();
        foreach (var result in results)
        {
            var source = result.Source.HasValue ? $" [{result.Source}]" : string.Empty;
            sb.AppendLine($"- {result.Kind}: {result.Name}{source}");
            sb.AppendLine($"  {result.Snippet}");
        }

        return TrimForTool(sb.ToString());
    }

    [ToolFunction(
        Name = "save_ai_skill",
        Description = """
DB에 저장되는 AI Skill을 추가하거나 갱신합니다. 로컬 source-of-truth Skill 이름으로는 저장할 수 없습니다.
저장 전 read_ai_skill 또는 search_ai_configuration으로 기존 내용을 확인하세요. trigger_phrases는 줄바꿈 또는 콤마로 구분합니다.
이 도구는 AI 설정 변경 권한이 있는 Discord 사용자에게만 동작합니다.
""")]
    public async Task<string> SaveAiSkillAsync(
        [ToolParameterInfo(Description = "AI Skill 이름. 소문자, 숫자, 하이픈만 허용됩니다.")]
        string name,
        [ToolParameterInfo(Description = "AI Skill 설명.")]
        string description,
        [ToolParameterInfo(Description = "우선순위. 높을수록 먼저 선택됩니다.")]
        int priority,
        [ToolParameterInfo(Description = "trigger phrase 목록. 줄바꿈 또는 콤마로 구분합니다.")]
        string trigger_phrases,
        [ToolParameterInfo(Description = "AI Skill 지침 본문.")]
        string instructions,
        [ToolParameterInfo(Description = "Skill 활성 여부.")]
        bool enabled = true,
        CancellationToken cancellationToken = default)
    {
        if (!IsAuthorized(out var error))
        {
            return error;
        }

        var phrases = SplitTriggerPhrases(trigger_phrases);
        await skillManagement.SaveDatabaseSkillAsync(
            new AiSkillSaveRequest(name, description, priority, phrases, instructions, enabled),
            cancellationToken);

        logger.LogInformation(
            "AI Skill {SkillName} saved by Discord user {UserId} via tool.",
            name,
            message.Author.Id);
        return $"AI Skill '{name}'을 저장했습니다.";
    }

    [ToolFunction(
        Name = "rename_ai_skill",
        Description = """
DB에 저장된 AI Skill 이름을 변경합니다. 로컬 source-of-truth Skill은 변경할 수 없습니다.
이 도구는 AI 설정 변경 권한이 있는 Discord 사용자에게만 동작합니다.
""")]
    public async Task<string> RenameAiSkillAsync(
        [ToolParameterInfo(Description = "기존 AI Skill 이름.")]
        string name,
        [ToolParameterInfo(Description = "새 AI Skill 이름. 소문자, 숫자, 하이픈만 허용됩니다.")]
        string new_name,
        CancellationToken cancellationToken = default)
    {
        if (!IsAuthorized(out var error))
        {
            return error;
        }

        await skillManagement.RenameDatabaseSkillAsync(name, new_name, cancellationToken);
        logger.LogInformation(
            "AI Skill {SkillName} renamed to {NewSkillName} by Discord user {UserId} via tool.",
            name,
            new_name,
            message.Author.Id);
        return $"AI Skill '{name}'의 이름을 '{new_name}'로 변경했습니다.";
    }

    [ToolFunction(
        Name = "delete_ai_skill",
        Description = """
DB에 저장된 AI Skill을 삭제합니다. 로컬 source-of-truth Skill은 삭제할 수 없습니다.
삭제 전 read_ai_skill로 대상이 맞는지 확인하세요. 이 도구는 AI 설정 변경 권한이 있는 Discord 사용자에게만 동작합니다.
""")]
    public async Task<string> DeleteAiSkillAsync(
        [ToolParameterInfo(Description = "삭제할 AI Skill 이름.")]
        string name,
        CancellationToken cancellationToken = default)
    {
        if (!IsAuthorized(out var error))
        {
            return error;
        }

        await skillManagement.DeleteDatabaseSkillAsync(name, cancellationToken);
        logger.LogInformation(
            "AI Skill {SkillName} deleted by Discord user {UserId} via tool.",
            name,
            message.Author.Id);
        return $"AI Skill '{name}'을 삭제했습니다.";
    }

    public string? GetToolFunctionDescription(string functionName)
    {
        return functionName switch
        {
            "read_ai_instructions" => "현재 DB 전역 AI 지침을 읽습니다. 설정 변경 요청에서는 먼저 읽고 난 뒤 수정하세요.",
            "save_ai_instructions" => "전역 AI 지침 전체 본문을 저장합니다. 부분 수정이 아니라 전체 교체입니다.",
            "list_ai_skills" => "DB Skill과 로컬 source-of-truth Skill 목록을 읽습니다.",
            "read_ai_skill" => "특정 AI Skill의 전체 내용을 읽습니다.",
            "search_ai_configuration" => "AI 지침과 AI Skill을 키워드로 검색합니다.",
            "save_ai_skill" => "DB Skill을 추가하거나 갱신합니다. 로컬 Skill은 수정할 수 없습니다.",
            "rename_ai_skill" => "DB Skill 이름을 변경합니다.",
            "delete_ai_skill" => "DB Skill을 삭제합니다.",
            _ => null
        };
    }

    private bool IsAuthorized(out string error)
    {
        var allowedUserIds = options.Value.AllowedDiscordUserIds
            .Select(id => id.Trim())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal);

        if (allowedUserIds.Contains(message.Author.Id.ToString()))
        {
            error = string.Empty;
            return true;
        }

        error = "권한이 없습니다. AI 설정 수정 도구를 사용하려면 AiConfigurationManagement:AllowedDiscordUserIds에 Discord 사용자 ID를 등록해야 합니다.";
        return false;
    }

    private static IReadOnlyList<string> SplitTriggerPhrases(string value)
    {
        return value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split(['\n', ','], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }

    private static string FormatSkill(AiSkillManagementView skill, int offset, int maxCharacters)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"name: {skill.Name}");
        sb.AppendLine($"source: {skill.Source}");
        sb.AppendLine($"editable: {skill.IsEditable}");
        sb.AppendLine($"enabled: {skill.Enabled}");
        sb.AppendLine($"priority: {skill.Priority}");
        sb.AppendLine($"description: {skill.Description}");
        sb.AppendLine($"trigger_phrases: {string.Join(", ", skill.TriggerPhrases)}");
        if (skill.ToolNames.Count > 0)
        {
            sb.AppendLine($"tool_names: {string.Join(", ", skill.ToolNames)}");
        }

        sb.AppendLine();
        sb.AppendLine("[instructions]");
        sb.AppendLine(SliceForTool(skill.Instructions, offset, maxCharacters));

        return sb.ToString();
    }

    private static string TrimForTool(string value)
    {
        if (value.Length <= MaxReturnedCharacters)
        {
            return value;
        }

        return value[..MaxReturnedCharacters] + "\n...(truncated)";
    }

    private static string SliceForTool(string value, int offset, int maxCharacters)
    {
        offset = Math.Clamp(offset, 0, value.Length);
        maxCharacters = Math.Clamp(maxCharacters, 1, MaxReturnedCharacters);
        var length = Math.Min(maxCharacters, value.Length - offset);
        var slice = value.Substring(offset, length);
        var end = offset + length;

        if (end >= value.Length)
        {
            return slice;
        }

        return slice + $"\n...(truncated; next_offset={end}; total_length={value.Length})";
    }
}
