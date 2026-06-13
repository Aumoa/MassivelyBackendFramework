namespace DiscordBot.Options;

public class AttachmentProcessingOptions
{
    public int MaxAttachmentBytes { get; set; } = 8 * 1024 * 1024;

    public int MaxExtractedTextCharacters { get; set; } = 200_000;

    public int MaxPromptTextCharacters { get; set; } = 24_000;

    public string[] AllowedExtensions { get; set; } =
    [
        ".txt",
        ".md",
        ".markdown",
        ".csv",
        ".json",
        ".log",
        ".pdf"
    ];

    public string[] AllowedContentTypes { get; set; } =
    [
        "text/plain",
        "text/markdown",
        "text/csv",
        "application/json",
        "application/pdf"
    ];
}
