namespace AI.Providers.Claude;

public class ClaudeChatClientOptions
{
    public string ApiKey { get; set; } = "";

    public string BaseUri { get; set; } = "https://api.anthropic.com";

    public string AnthropicVersion { get; set; } = "2023-06-01";

    public int DefaultMaxTokens { get; set; } = 16000;
}
