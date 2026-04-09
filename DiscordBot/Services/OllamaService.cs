using Discord;
using Microsoft.Extensions.Options;

namespace DiscordBot.Services;

public class OllamaService(ILogger<OllamaService> logger, IOptions<OllamaService.Configuration> options, HttpClient http)
{
    public record Configuration
    {
        public required string Uri { get; set; }
        public string? Persona { get; set; }
        public required int MemorySize { get; set; }
        public required string Model { get; set; }
        public required string KeepAlive { get; set; }
        public required string SummaryModel { get; set; }
        public required string SummaryKeepAlive { get; set; }
    }

    private readonly Dictionary<ulong, OllamaChatHistory> m_Chats = [];

    public OllamaChatHistory GetChannel(IChannel channel)
    {
        lock (m_Chats)
        {
            if (m_Chats.TryGetValue(channel.Id, out var chatHistory) == false)
            {
                chatHistory = new OllamaChatHistory(logger, options.Value, http);
                m_Chats.Add(channel.Id, chatHistory);
            }

            return chatHistory;
        }
    }
}
