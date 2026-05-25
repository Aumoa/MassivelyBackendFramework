using Dapper;
using DiscordBot.Options;
using Microsoft.Extensions.Options;

namespace DiscordBot.Repositories;

internal class MySqlChatImageRepository(IOptions<MySqlOptions> options)
    : MySqlDbContext(options.Value), IChatImageRepository
{
    public async ValueTask<ChatImageData?> GetLatestAsync(
        string channelId,
        DateTimeOffset before,
        CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY = @"
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
JOIN `chat_log` l ON l.`id` = i.`chat_log_id`
WHERE l.`channel_id` = @channelId
  AND l.`created_at` < @before
ORDER BY l.`created_at` DESC, i.`id`
LIMIT 1";

        var command = new CommandDefinition(
            QUERY,
            new
            {
                channelId,
                before = before.UtcDateTime
            },
            cancellationToken: cancellationToken);

        return await connection.QueryFirstOrDefaultAsync<ChatImageData>(command);
    }

    public async ValueTask<ChatImageData?> GetByMessageIdAsync(
        string channelId,
        string messageId,
        CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY = @"
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
JOIN `chat_log` l ON l.`id` = i.`chat_log_id`
WHERE l.`channel_id` = @channelId
  AND l.`message_id` = @messageId
ORDER BY i.`id`
LIMIT 1";

        var command = new CommandDefinition(
            QUERY,
            new
            {
                channelId,
                messageId
            },
            cancellationToken: cancellationToken);

        return await connection.QueryFirstOrDefaultAsync<ChatImageData>(command);
    }
}
