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

        if (!string.IsNullOrWhiteSpace(profile.QualityPositive))
        {
            sb.AppendLine("[공통 긍정 품질 태그]");
            sb.AppendLine(profile.QualityPositive.Trim());
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(profile.QualityNegative))
        {
            sb.AppendLine("[공통 부정 품질 태그]");
            sb.AppendLine(profile.QualityNegative.Trim());
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

        public string QualityPositive { get; init; } = "";

        public string QualityNegative { get; init; } = "";

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
