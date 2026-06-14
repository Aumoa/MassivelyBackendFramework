using Dapper;
using DiscordBot.Options;
using Microsoft.Extensions.Options;

namespace DiscordBot.Repositories;

internal class MySqlChatAttachmentRepository(IOptions<MySqlOptions> options)
    : MySqlDbContext(options.Value), IChatAttachmentRepository
{
    private const string SelectColumns = @"
SELECT
    a.`id` AS Id,
    a.`chat_log_id` AS ChatLogId,
    l.`message_id` AS MessageId,
    l.`guild_id` AS GuildId,
    l.`channel_id` AS ChannelId,
    l.`user_id` AS UserId,
    l.`content` AS Content,
    a.`discord_attachment_id` AS DiscordAttachmentId,
    a.`file_name` AS FileName,
    a.`content_type` AS ContentType,
    a.`size_bytes` AS SizeBytes,
    a.`sha256` AS Sha256,
    a.`extracted_text` AS ExtractedText,
    a.`extraction_status` AS ExtractionStatus,
    a.`extraction_error` AS ExtractionError,
    a.`created_at` AS CreatedAt
FROM `chat_log_attachment` a
JOIN `chat_log` l ON l.`id` = a.`chat_log_id`";

    public async ValueTask<ChatAttachmentData?> GetLatestAsync(
        string channelId,
        DateTimeOffset before,
        CancellationToken cancellationToken = default)
    {
        var attachments = await GetLatestAsync(channelId, before, 1, cancellationToken);
        return attachments.FirstOrDefault();
    }

    public async ValueTask<IReadOnlyList<ChatAttachmentData>> GetLatestAsync(
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
ORDER BY l.`created_at` DESC, a.`id`
LIMIT @limit",
            new
            {
                channelId,
                before = before.UtcDateTime,
                limit
            },
            cancellationToken: cancellationToken);

        var results = await connection.QueryAsync<ChatAttachmentData>(command);
        return results.ToList();
    }

    public async ValueTask<IReadOnlyList<ChatAttachmentData>> GetByMessageIdAsync(
        string channelId,
        string messageId,
        CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        var command = new CommandDefinition(
            SelectColumns + @"
WHERE l.`channel_id` = @channelId
  AND l.`message_id` = @messageId
ORDER BY a.`id`",
            new
            {
                channelId,
                messageId
            },
            cancellationToken: cancellationToken);

        var results = await connection.QueryAsync<ChatAttachmentData>(command);
        return results.ToList();
    }

    public async ValueTask<IReadOnlyList<ChatAttachmentData>> GetByMessageIdsAsync(
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
ORDER BY FIND_IN_SET(l.`message_id`, @messageIdOrder), a.`id`
LIMIT @limit",
            new
            {
                channelId,
                messageIds,
                messageIdOrder,
                limit
            },
            cancellationToken: cancellationToken);

        var results = await connection.QueryAsync<ChatAttachmentData>(command);
        return results.ToList();
    }

    public async ValueTask<IReadOnlyList<ChatAttachmentData>> SearchAsync(
        string channelId,
        IReadOnlyList<string> keywords,
        int limit,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken cancellationToken = default)
    {
        if (keywords.Count == 0)
        {
            return [];
        }

        var searchQuery = string.Join(' ', keywords
            .Select(static keyword => keyword.Trim())
            .Where(static keyword => keyword.Length >= 2)
            .Select(static keyword => $"\"{keyword.Replace("\"", "")}\""));

        if (string.IsNullOrWhiteSpace(searchQuery))
        {
            return [];
        }

        using var connection = GetConnection();
        var queryBuilder = new System.Text.StringBuilder(SelectColumns);
        queryBuilder.Append(@"
WHERE l.`channel_id` = @channelId
  AND a.`extracted_text` IS NOT NULL
  AND MATCH(a.`extracted_text`) AGAINST(@searchQuery IN BOOLEAN MODE)");

        if (from.HasValue)
        {
            queryBuilder.Append(" AND l.`created_at` >= @from");
        }

        if (to.HasValue)
        {
            queryBuilder.Append(" AND l.`created_at` <= @to");
        }

        queryBuilder.Append(@"
ORDER BY MATCH(a.`extracted_text`) AGAINST(@searchQuery IN BOOLEAN MODE) DESC,
         l.`created_at` DESC,
         a.`id`
LIMIT @limit");

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

        var results = await connection.QueryAsync<ChatAttachmentData>(command);
        return results.ToList();
    }
}
