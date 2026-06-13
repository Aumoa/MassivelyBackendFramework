namespace DiscordBot.Options;

public class AttachmentDownloadOptions
{
    public const string HttpClientName = "DiscordAttachments";

    public int TimeoutSeconds { get; set; } = 30;

    public int MaxImageBytes { get; set; } = 32 * 1024 * 1024;
}
