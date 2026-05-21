namespace DiscordBot.Options;

public class ImageGenerationOptions
{
    public string BaseUrl { get; set; } = "http://localhost:8188";

    public string WorkflowPath { get; set; } = Path.Combine("ImageGeneration", "Workflows", "anime-xl.api.json");

    public string PromptProfilePath { get; set; } = Path.Combine("ImageGeneration", "PromptProfiles", "anime-xl.json");

    public string ClientId { get; set; } = "discordbot";

    public string PositivePromptTitle { get; set; } = "PositivePrompt";

    public string NegativePromptTitle { get; set; } = "NegativePrompt";

    public string SaveImageTitle { get; set; } = "SaveImage";

    public string FilenamePrefix { get; set; } = "DiscordBot";

    public int TimeoutSeconds { get; set; } = 600;

    public int PollIntervalMilliseconds { get; set; } = 2000;

    public int MaxConcurrentJobs { get; set; } = 1;
}
