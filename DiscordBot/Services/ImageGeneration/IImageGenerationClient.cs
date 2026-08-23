namespace DiscordBot.Services.ImageGeneration;

public interface IImageGenerationClient
{
    Task<GeneratedImage> GenerateAsync(
        string positivePrompt,
        IProgress<ImageGenerationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
