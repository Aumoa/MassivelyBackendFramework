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

    public async ValueTask<IReadOnlyList<ChatLogData>> GetAsync(string channelId, int limit, int offset = 0,
        DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        var queryBuilder = new System.Text.StringBuilder(@"
SELECT `id` AS Id, `channel_id` AS ChannelId, `user_id` AS UserId, `content` AS Content, `created_at` AS CreatedAt
FROM `chat_log`
WHERE `channel_id` = @channelId");

        if (from.HasValue)
            queryBuilder.Append(" AND `created_at` >= @from");
        if (to.HasValue)
            queryBuilder.Append(" AND `created_at` <= @to");

        queryBuilder.Append(" ORDER BY `created_at` DESC LIMIT @limit OFFSET @offset");

        var command = new CommandDefinition(
            queryBuilder.ToString(),
            new
            {
                channelId,
                limit,
                offset,
                from = from?.UtcDateTime,
                to = to?.UtcDateTime
            },
            cancellationToken: cancellationToken);

        var results = await connection.QueryAsync<ChatLogData>(command);
        return results.Reverse().ToList();
    }
}
