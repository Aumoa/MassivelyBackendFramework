using System.Text;
using System.Text.Json;
using DiscordBot.Options;
using Microsoft.Extensions.Options;

namespace DiscordBot.Services.ImageGeneration;

public sealed class ImagePromptProfileProvider(
    IOptions<ImageGenerationOptions> options,
    IHostEnvironment environment,
    ILogger<ImagePromptProfileProvider> logger)
{
    private readonly ImageGenerationOptions m_Options = options.Value;

    public string BuildToolDescription(string baseDescription)
    {
        var profile = LoadProfile();
        if (profile == null)
        {
            return baseDescription;
        }

        var sb = new StringBuilder(baseDescription.Trim());
        sb.AppendLine();
        sb.AppendLine();

        if (profile.Rules.Count > 0)
        {
            sb.AppendLine("[프롬프트 작성 규칙]");
            foreach (var rule in profile.Rules)
            {
                sb.AppendLine("- " + rule);
            }
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(profile.FixedPositive))
        {
            sb.AppendLine("[고정 긍정 품질 태그 (시스템이 자동 추가)]");
            sb.AppendLine(profile.FixedPositive.Trim());
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(profile.FixedNegative))
        {
            sb.AppendLine("[고정 부정 품질 태그 (시스템이 자동 추가)]");
            sb.AppendLine(profile.FixedNegative.Trim());
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(profile.RecommendedPositive))
        {
            sb.AppendLine("[권장 긍정 태그 (기본 포함, 요청과 충돌 시 제외 가능)]");
            sb.AppendLine(profile.RecommendedPositive.Trim());
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(profile.RecommendedNegative))
        {
            sb.AppendLine("[권장 부정 태그 (기본 포함, 요청과 충돌 시 제외 가능)]");
            sb.AppendLine(profile.RecommendedNegative.Trim());
            sb.AppendLine();
        }

        for (int i = 0; i < profile.Examples.Count; i++)
        {
            var example = profile.Examples[i];
            sb.AppendLine($"[대표 예시 {i + 1}]");
            sb.AppendLine("사용자 요청: " + example.UserRequest);
            sb.AppendLine("positive_prompt: " + example.Positive);
            sb.AppendLine("negative_prompt: " + example.Negative);
            if (i < profile.Examples.Count - 1)
            {
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    public string BuildPromptGenerationSystem()
    {
        var sb = new StringBuilder();
        sb.AppendLine("너는 Discord 이미지 생성 도구 내부의 프롬프트 작성기입니다.");
        sb.AppendLine("사용자의 이미지 요청을 ComfyUI용 positive_prompt와 negative_prompt로 변환하세요.");
        sb.AppendLine("출력은 JSON 객체 하나만 허용됩니다. 마크다운 코드 블록, 설명, 주석을 쓰지 마세요.");
        sb.AppendLine(@"출력 형식: {""positive_prompt"":""..."",""negative_prompt"":""...""}");
        sb.AppendLine("positive_prompt와 negative_prompt는 영어 태그와 짧은 영어 구문 중심으로 작성하세요.");
        sb.AppendLine("사용자 요청에 없는 구체적인 캐릭터 특징, 머리색, 의상, 배경, 구도는 예시에서 가져오지 마세요.");
        sb.AppendLine("고정 품질 태그는 시스템이 응답 이후 자동으로 추가하므로, positive_prompt와 negative_prompt에 절대 포함하지 마세요.");
        sb.AppendLine("권장 태그는 기본적으로 positive_prompt 또는 negative_prompt에 포함하되, 사용자 요청과 형식적으로 충돌하는 개별 항목만 제외하세요.");
        sb.AppendLine();

        var profile = LoadProfile();
        if (profile == null)
        {
            return sb.ToString();
        }

        if (profile.Rules.Count > 0)
        {
            sb.AppendLine("[프롬프트 작성 규칙]");
            foreach (var rule in profile.Rules)
            {
                if (rule.Contains("generate_image", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                sb.AppendLine("- " + rule);
            }
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(profile.RecommendedPositive))
        {
            sb.AppendLine("[positive_prompt에 기본 포함할 권장 태그 (충돌 시에만 제외)]");
            sb.AppendLine(profile.RecommendedPositive.Trim());
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(profile.RecommendedNegative))
        {
            sb.AppendLine("[negative_prompt에 기본 포함할 권장 태그 (충돌 시에만 제외)]");
            sb.AppendLine(profile.RecommendedNegative.Trim());
            sb.AppendLine();
        }

        for (int i = 0; i < profile.Examples.Count; i++)
        {
            var example = profile.Examples[i];
            sb.AppendLine($"[대표 예시 {i + 1}]");
            sb.AppendLine("사용자 요청: " + example.UserRequest);
            sb.AppendLine("positive_prompt: " + example.Positive);
            sb.AppendLine("negative_prompt: " + example.Negative);
            if (i < profile.Examples.Count - 1)
            {
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    public (string PositivePrompt, string NegativePrompt) BuildFallbackPrompts(string userRequest)
    {
        var profile = LoadProfile();
        var positivePrompt = userRequest.Trim();
        var negativePrompt = string.Empty;

        if (profile == null)
        {
            return (positivePrompt, negativePrompt);
        }

        if (!string.IsNullOrWhiteSpace(profile.RecommendedPositive))
        {
            positivePrompt = string.IsNullOrWhiteSpace(positivePrompt)
                ? profile.RecommendedPositive.Trim()
                : positivePrompt + ", " + profile.RecommendedPositive.Trim();
        }

        if (!string.IsNullOrWhiteSpace(profile.RecommendedNegative))
        {
            negativePrompt = profile.RecommendedNegative.Trim();
        }

        return (positivePrompt, negativePrompt);
    }

    // LLM 초안/폴백 결과와 무관하게 고정 품질 태그를 항상 강제로 덧붙인다.
    public (string PositivePrompt, string NegativePrompt) ApplyFixedTags(string positivePrompt, string negativePrompt)
    {
        var profile = LoadProfile();
        if (profile == null)
        {
            return (positivePrompt, negativePrompt);
        }

        var mergedPositive = string.IsNullOrWhiteSpace(profile.FixedPositive)
            ? positivePrompt
            : Combine(profile.FixedPositive.Trim(), positivePrompt);

        var mergedNegative = string.IsNullOrWhiteSpace(profile.FixedNegative)
            ? negativePrompt
            : Combine(profile.FixedNegative.Trim(), negativePrompt);

        return (mergedPositive, mergedNegative);
    }

    private static string Combine(string fixedTags, string rest)
    {
        return string.IsNullOrWhiteSpace(rest)
            ? fixedTags
            : fixedTags + ", " + rest.Trim();
    }

    private ImagePromptProfile? LoadProfile()
    {
        var path = m_Options.PromptProfilePath;
        if (!Path.IsPathRooted(path))
        {
            path = Path.Combine(environment.ContentRootPath, path);
        }

        if (!File.Exists(path))
        {
            logger.LogWarning("Image prompt profile was not found: {Path}", path);
            return null;
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize(json, ImagePromptProfileJsonContext.Default.ImagePromptProfile);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to load image prompt profile: {Path}", path);
            return null;
        }
    }

    public sealed record ImagePromptProfile
    {
        public string Name { get; init; } = "";

        public string FixedPositive { get; init; } = "";

        public string FixedNegative { get; init; } = "";

        public string RecommendedPositive { get; init; } = "";

        public string RecommendedNegative { get; init; } = "";

        public List<string> Rules { get; init; } = [];

        public List<ImagePromptExample> Examples { get; init; } = [];
    }

    public sealed record ImagePromptExample
    {
        public string UserRequest { get; init; } = "";

        public string Positive { get; init; } = "";

        public string Negative { get; init; } = "";
    }
}
