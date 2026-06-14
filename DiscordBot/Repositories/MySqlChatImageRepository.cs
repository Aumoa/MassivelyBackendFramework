using Dapper;
using DiscordBot.Options;
using Microsoft.Extensions.Options;

namespace DiscordBot.Repositories;

internal class MySqlChatImageRepository(IOptions<MySqlOptions> options)
    : MySqlDbContext(options.Value), IChatImageRepository
{
    private const string SelectColumns = @"
SELECT
    i.`id` AS Id,
    i.`chat_log_id` AS ChatLogId,
    l.`message_id` AS MessageId,
    l.`guild_id` AS GuildId,
    l.`channel_id` AS ChannelId,
    l.`user_id` AS UserId,
    l.`content` AS Content,
    i.`file_name` AS FileName,
    i.`content_type` AS ContentType,
    i.`width` AS Width,
    i.`height` AS Height,
    i.`data` AS Data,
    i.`created_at` AS CreatedAt
FROM `chat_log_image` i
JOIN `chat_log` l ON l.`id` = i.`chat_log_id`";

    public async ValueTask<ChatImageData?> GetLatestAsync(
        string channelId,
        DateTimeOffset before,
        CancellationToken cancellationToken = default)
    {
        var images = await GetLatestAsync(channelId, before, 1, cancellationToken);
        return images.FirstOrDefault();
    }

    public async ValueTask<IReadOnlyList<ChatImageData>> GetLatestAsync(
        string channelId,
        DateTimeOffset before,
        int limit,
        CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        var command = new CommandDefinition(
            SelectColumns + @"
WHERE l.`channel_id` = @channelId
  AND l.`created_at` < @before
ORDER BY l.`created_at` DESC, i.`id`
LIMIT @limit",
            new
            {
                channelId,
                before = before.UtcDateTime,
                limit
            },
            cancellationToken: cancellationToken);

        var results = await connection.QueryAsync<ChatImageData>(command);
        return results.ToList();
    }

    public async ValueTask<ChatImageData?> GetByMessageIdAsync(
        string channelId,
        string messageId,
        CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        var command = new CommandDefinition(
            SelectColumns + @"
WHERE l.`channel_id` = @channelId
  AND l.`message_id` = @messageId
ORDER BY i.`id`
LIMIT 1",
            new
            {
                channelId,
                messageId
            },
            cancellationToken: cancellationToken);

        return await connection.QueryFirstOrDefaultAsync<ChatImageData>(command);
    }

    public async ValueTask<IReadOnlyList<ChatImageData>> GetByMessageIdsAsync(
        string channelId,
        IReadOnlyList<string> messageIds,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (messageIds.Count == 0)
        {
            return [];
        }

        using var connection = GetConnection();
        var messageIdOrder = string.Join(',', messageIds);

        var command = new CommandDefinition(
            SelectColumns + @"
WHERE l.`channel_id` = @channelId
  AND l.`message_id` IN @messageIds
ORDER BY FIND_IN_SET(l.`message_id`, @messageIdOrder), i.`id`
LIMIT @limit",
            new
            {
                channelId,
                messageIds,
                messageIdOrder,
                limit
            },
            cancellationToken: cancellationToken);

        var results = await connection.QueryAsync<ChatImageData>(command);
        return results.ToList();
    }
}
