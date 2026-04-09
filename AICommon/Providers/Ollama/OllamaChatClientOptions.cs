namespace AI.Providers.Ollama;

public class OllamaChatClientOptions
{
    public string Uri { get; set; } = "";

    public string KeepAlive { get; set; } = "5m";
}
