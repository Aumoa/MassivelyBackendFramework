namespace DiscordBot.Options;

public sealed class DailyImagePostOptions
{
    public bool Enabled { get; set; }

    public string ChannelId { get; set; } = "";

    public string PostTimeOfDay { get; set; } = "09:00";

    public string ThemePrompt { get; set; } = "";
}
