using Dapper;
using DiscordBot.Options;
using Microsoft.Extensions.Options;

namespace DiscordBot.Repositories;

internal class MySqlChatLogRepository(IOptions<MySqlOptions> options)
    : MySqlDbContext(options.Value), IChatLogRepository
{
    public async ValueTask AddAsync(string channelId, string userId, string content, CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY = @"
INSERT INTO `chat_log` (`channel_id`, `user_id`, `content`)
VALUES(@channelId, @userId, @content)";

        var command = new CommandDefinition(
            QUERY,
            new { channelId, userId, content },
            cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }
}
