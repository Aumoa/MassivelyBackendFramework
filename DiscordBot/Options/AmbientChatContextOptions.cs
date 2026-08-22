namespace DiscordBot.Options;

public sealed class AmbientChatContextOptions
{
    public bool Enabled { get; set; }

    public int WindowMessageCount { get; set; } = 12;

    public int WindowMaxChars { get; set; } = 2000;

    public int LookbackMinutes { get; set; } = 60;
}
