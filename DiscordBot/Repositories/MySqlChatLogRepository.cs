using Dapper;
using DiscordBot.Options;
using Microsoft.Extensions.Options;

namespace DiscordBot.Repositories;

internal class MySqlChatLogRepository(IOptions<MySqlOptions> options)
    : MySqlDbContext(options.Value), IChatLogRepository
{
    private const string SELECT_CHAT_LOG_COLUMNS = @"
`id` AS Id,
`message_id` AS MessageId,
`guild_id` AS GuildId,
`channel_id` AS ChannelId,
`user_id` AS UserId,
`content` AS Content,
`created_at` AS CreatedAt,
`referenced_message_id` AS ReferencedMessageId,
`referenced_channel_id` AS ReferencedChannelId,
`referenced_guild_id` AS ReferencedGuildId";

    public async ValueTask AddAsync(
        string? messageId,
        string? guildId,
        string channelId,
        string userId,
        string content,
        IReadOnlyList<ChatLogImageInput>? images = null,
        IReadOnlyList<ChatLogAttachmentInput>? attachments = null,
        string? referencedMessageId = null,
        string? referencedChannelId = null,
        string? referencedGuildId = null,
        CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();
        await connection.OpenAsync(cancellationToken);
        using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        const string INSERT_CHAT_LOG_QUERY = @"
INSERT INTO `chat_log`
    (`message_id`, `guild_id`, `channel_id`, `user_id`, `content`, `referenced_message_id`, `referenced_channel_id`, `referenced_guild_id`)
VALUES
    (@messageId, @guildId, @channelId, @userId, @content, @referencedMessageId, @referencedChannelId, @referencedGuildId)";

        var chatLogCommand = new CommandDefinition(
            INSERT_CHAT_LOG_QUERY,
            new
            {
                messageId,
                guildId,
                channelId,
                userId,
                content,
                referencedMessageId,
                referencedChannelId,
                referencedGuildId
            },
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

        if (attachments is { Count: > 0 })
        {
            const string INSERT_ATTACHMENT_QUERY = @"
INSERT INTO `chat_log_attachment`
    (`chat_log_id`, `discord_attachment_id`, `file_name`, `content_type`, `size_bytes`, `sha256`, `data`, `extracted_text`, `extraction_status`, `extraction_error`)
VALUES
    (@chatLogId, @discordAttachmentId, @fileName, @contentType, @sizeBytes, @sha256, @data, @extractedText, @extractionStatus, @extractionError)";

            foreach (var attachment in attachments)
            {
                var attachmentCommand = new CommandDefinition(
                    INSERT_ATTACHMENT_QUERY,
                    new
                    {
                        chatLogId,
                        discordAttachmentId = attachment.DiscordAttachmentId,
                        fileName = attachment.FileName,
                        contentType = attachment.ContentType,
                        sizeBytes = attachment.SizeBytes,
                        sha256 = attachment.Sha256,
                        data = attachment.Data,
                        extractedText = attachment.ExtractedText,
                        extractionStatus = attachment.ExtractionStatus,
                        extractionError = attachment.ExtractionError
                    },
                    transaction,
                    cancellationToken: cancellationToken);
                await connection.ExecuteAsync(attachmentCommand);
            }
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async ValueTask<IReadOnlyList<ChatLogData>> GetAsync(string channelId, int limit, int offset = 0,
        DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        var queryBuilder = new System.Text.StringBuilder(@"
SELECT " + SELECT_CHAT_LOG_COLUMNS + @"
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
SELECT " + SELECT_CHAT_LOG_COLUMNS + @"
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

    public async ValueTask<IReadOnlyList<ChatLogData>> GetContextAsync(
        string channelId,
        long chatLogId,
        int before,
        int after,
        CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string TARGET_QUERY = @"
SELECT " + SELECT_CHAT_LOG_COLUMNS + @"
FROM `chat_log`
WHERE `channel_id` = @channelId
  AND `id` = @chatLogId
LIMIT 1";

        var targetCommand = new CommandDefinition(
            TARGET_QUERY,
            new { channelId, chatLogId },
            cancellationToken: cancellationToken);
        var target = await connection.QueryFirstOrDefaultAsync<ChatLogData>(targetCommand);
        if (target == null)
        {
            return [];
        }

        const string BEFORE_QUERY = @"
SELECT " + SELECT_CHAT_LOG_COLUMNS + @"
FROM `chat_log`
WHERE `channel_id` = @channelId
  AND (`created_at` < @createdAt OR (`created_at` = @createdAt AND `id` < @chatLogId))
ORDER BY `created_at` DESC, `id` DESC
LIMIT @before";

        const string AFTER_QUERY = @"
SELECT " + SELECT_CHAT_LOG_COLUMNS + @"
FROM `chat_log`
WHERE `channel_id` = @channelId
  AND (`created_at` > @createdAt OR (`created_at` = @createdAt AND `id` > @chatLogId))
ORDER BY `created_at` ASC, `id` ASC
LIMIT @after";

        var args = new
        {
            channelId,
            chatLogId,
            createdAt = target.CreatedAt,
            before,
            after
        };

        var beforeCommand = new CommandDefinition(BEFORE_QUERY, args, cancellationToken: cancellationToken);
        var afterCommand = new CommandDefinition(AFTER_QUERY, args, cancellationToken: cancellationToken);
        var beforeItems = (await connection.QueryAsync<ChatLogData>(beforeCommand)).Reverse();
        var afterItems = await connection.QueryAsync<ChatLogData>(afterCommand);

        return beforeItems
            .Concat([target])
            .Concat(afterItems)
            .ToList();
    }

    public async ValueTask<ChatLogData?> GetByMessageIdAsync(
        string channelId,
        string messageId,
        CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY = @"
SELECT " + SELECT_CHAT_LOG_COLUMNS + @"
FROM `chat_log`
WHERE `channel_id` = @channelId
  AND `message_id` = @messageId
LIMIT 1";

        var command = new CommandDefinition(
            QUERY,
            new { channelId, messageId },
            cancellationToken: cancellationToken);

        return await connection.QueryFirstOrDefaultAsync<ChatLogData>(command);
    }
}
