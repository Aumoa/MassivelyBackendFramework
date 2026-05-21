namespace DiscordBot.Services.ImageGeneration;

public interface IImageGenerationClient
{
    Task<GeneratedImage> GenerateAsync(
        string positivePrompt,
        string negativePrompt,
        CancellationToken cancellationToken = default);
}
