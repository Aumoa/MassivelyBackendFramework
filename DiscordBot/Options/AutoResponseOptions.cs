namespace DiscordBot.Options;

public sealed class AutoResponseOptions
{
    public bool Enabled { get; set; }

    public int IntervalSeconds { get; set; } = 45;

    public int CooldownSeconds { get; set; } = 180;

    public int MaxBufferedMessages { get; set; } = 20;

    public int ClassifierMaxTokens { get; set; } = 160;

    public string? ClassifierModel { get; set; }

    public string[] BotNameAliases { get; set; } =
    [
        "봇",
        "AI",
        "에이아이",
        "인공지능"
    ];
}
