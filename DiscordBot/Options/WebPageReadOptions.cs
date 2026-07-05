namespace DiscordBot.Options;

public sealed class WebPageReadOptions
{
    public const string HttpClientName = "DiscordWebPageReader";

    public int TimeoutSeconds { get; set; } = 15;

    public int MaxBytes { get; set; } = 1_000_000;

    public int DefaultMaxCharacters { get; set; } = 12_000;

    public int MaxCharacters { get; set; } = 50_000;

    public int MaxRedirects { get; set; } = 5;
}
