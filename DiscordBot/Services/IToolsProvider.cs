namespace DiscordBot.Services;

public interface IToolsProvider
{
    Task<string> GetMessagesAsync(int limit, CancellationToken cancellationToken = default);
    Task<string> GetCurrentDateAsync(CancellationToken cancellationToken = default);
}
