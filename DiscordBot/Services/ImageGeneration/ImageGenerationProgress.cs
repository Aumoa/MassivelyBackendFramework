namespace DiscordBot.Services.ImageGeneration;

public sealed record ImageGenerationProgress(
    string Status,
    string? NodeTitle = null,
    int? Value = null,
    int? Max = null);
