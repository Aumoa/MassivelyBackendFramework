using Dapper;
using DiscordBot.Options;
using Microsoft.Extensions.Options;

namespace DiscordBot.Repositories;

internal class MySqlChatLogRepository(IOptions<MySqlOptions> options)
    : MySqlDbContext(options.Value), IChatLogRepository
{
    public async ValueTask AddAsync(
        string? messageId,
        string? guildId,
        string channelId,
        string userId,
        string content,
        IReadOnlyList<ChatLogImageInput>? images = null,
        CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();
        await connection.OpenAsync(cancellationToken);
        using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        const string INSERT_CHAT_LOG_QUERY = @"
INSERT INTO `chat_log` (`message_id`, `guild_id`, `channel_id`, `user_id`, `content`)
VALUES(@messageId, @guildId, @channelId, @userId, @content)";

        var chatLogCommand = new CommandDefinition(
            INSERT_CHAT_LOG_QUERY,
            new { messageId, guildId, channelId, userId, content },
            transaction,
            cancellationToken: cancellationToken);
        await connection.ExecuteAsync(chatLogCommand);

        var chatLogIdCommand = new CommandDefinition(
            "SELECT LAST_INSERT_ID()",
            transaction: transaction,
            cancellationToken: cancellationToken);
        var chatLogId = await connection.ExecuteScalarAsync<long>(chatLogIdCommand);

        if (images is { Count: > 0 })
        {
            const string INSERT_IMAGE_QUERY = @"
INSERT INTO `chat_log_image`
    (`chat_log_id`, `file_name`, `content_type`, `width`, `height`, `data`)
VALUES
    (@chatLogId, @fileName, @contentType, @width, @height, @data)";

            foreach (var image in images)
            {
                var imageCommand = new CommandDefinition(
                    INSERT_IMAGE_QUERY,
                    new
                    {
                        chatLogId,
                        fileName = image.FileName,
                        contentType = image.ContentType,
                        width = image.Width,
                        height = image.Height,
                        data = image.Data
                    },
                    transaction,
                    cancellationToken: cancellationToken);
                await connection.ExecuteAsync(imageCommand);
            }
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async ValueTask<IReadOnlyList<ChatLogData>> GetAsync(string channelId, int limit, int offset = 0,
        DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        var queryBuilder = new System.Text.StringBuilder(@"
SELECT `id` AS Id, `message_id` AS MessageId, `guild_id` AS GuildId, `channel_id` AS ChannelId, `user_id` AS UserId, `content` AS Content, `created_at` AS CreatedAt
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

    public async ValueTask<IReadOnlyList<ChatLogData>> SearchAsync(string channelId, IReadOnlyList<string> keywords, int limit,
        DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken cancellationToken = default)
    {
        if (keywords.Count == 0)
            return [];

        using var connection = GetConnection();

        var searchQuery = string.Join(' ', keywords
            .Select(k => k.Trim())
            .Where(k => k.Length >= 2)
            .Select(k => $"\"{k.Replace("\"", "")}\""));

        if (string.IsNullOrWhiteSpace(searchQuery))
            return [];

        var queryBuilder = new System.Text.StringBuilder(@"
SELECT `id` AS Id, `message_id` AS MessageId, `guild_id` AS GuildId, `channel_id` AS ChannelId, `user_id` AS UserId, `content` AS Content, `created_at` AS CreatedAt
FROM `chat_log`
WHERE `channel_id` = @channelId
  AND MATCH(`content`) AGAINST(@searchQuery IN BOOLEAN MODE)");

        if (from.HasValue)
            queryBuilder.Append(" AND `created_at` >= @from");
        if (to.HasValue)
            queryBuilder.Append(" AND `created_at` <= @to");

        queryBuilder.Append(" ORDER BY MATCH(`content`) AGAINST(@searchQuery IN BOOLEAN MODE) DESC, `created_at` DESC LIMIT @limit");

        var command = new CommandDefinition(
            queryBuilder.ToString(),
            new
            {
                channelId,
                searchQuery,
                limit,
                from = from?.UtcDateTime,
                to = to?.UtcDateTime
            },
            cancellationToken: cancellationToken);

        var results = await connection.QueryAsync<ChatLogData>(command);
        return results.ToList();
    }
}
