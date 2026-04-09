using AI;
using Discord;
using Microsoft.Extensions.Options;

namespace DiscordBot.Services;

public class OllamaService(ILogger<OllamaService> logger, IOptions<OllamaService.Configuration> options, IChatClient chatClient)
{
    public record Configuration
    {
        public string? Persona { get; set; }
        public required int MemorySize { get; set; }
        public required string Model { get; set; }
        public required string SummaryModel { get; set; }
    }

    private readonly Dictionary<ulong, OllamaChatHistory> m_Chats = [];

    public OllamaChatHistory GetChannel(IChannel channel)
    {
        lock (m_Chats)
        {
            if (m_Chats.TryGetValue(channel.Id, out var chatHistory) == false)
            {
                chatHistory = new OllamaChatHistory(logger, options.Value, chatClient);
                m_Chats.Add(channel.Id, chatHistory);
            }

            return chatHistory;
        }
    }
}
