namespace DiscordBot.Services.ImageGeneration;

public sealed record GeneratedImage(
    byte[] Bytes,
    string FileName,
    string ContentType);
