namespace DiscordBot.Services.ImageGeneration;

public interface IImageGenerationClient
{
    Task<GeneratedImage> GenerateAsync(
        string positivePrompt,
        string negativePrompt,
        IProgress<ImageGenerationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
