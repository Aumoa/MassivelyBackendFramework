using Dapper;
using DiscordBot.Options;
using Microsoft.Extensions.Options;

namespace DiscordBot.Repositories;

internal sealed class MySqlChannelNoteRepository(IOptions<MySqlOptions> options)
    : MySqlDbContext(options.Value), IChannelNoteRepository
{
    public async ValueTask<long> AddAsync(ChannelNoteInput input, CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();
        await connection.OpenAsync(cancellationToken);

        const string QUERY = @"
INSERT INTO `channel_note`
    (`guild_id`, `channel_id`, `created_by_user_id`, `title`, `content`, `tags`, `source_message_id`)
VALUES
    (@GuildId, @ChannelId, @CreatedByUserId, @Title, @Content, @Tags, @SourceMessageId)";

        var command = new CommandDefinition(QUERY, input, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);

        var idCommand = new CommandDefinition(
            "SELECT LAST_INSERT_ID()",
            cancellationToken: cancellationToken);
        return await connection.ExecuteScalarAsync<long>(idCommand);
    }

    public async ValueTask<IReadOnlyList<ChannelNoteData>> GetActiveAsync(
        string channelId,
        string? guildId,
        int limit,
        string? query = null,
        CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        var queryBuilder = new System.Text.StringBuilder(@"
SELECT
    `id` AS Id,
    `guild_id` AS GuildId,
    `channel_id` AS ChannelId,
    `created_by_user_id` AS CreatedByUserId,
    `title` AS Title,
    `content` AS Content,
    `tags` AS Tags,
    `source_message_id` AS SourceMessageId,
    `status` AS Status,
    `created_at` AS CreatedAt,
    `updated_at` AS UpdatedAt
FROM `channel_note`
WHERE `channel_id` = CONVERT(@channelId USING utf8mb4) COLLATE utf8mb4_unicode_ci
  AND ((@guildId IS NULL AND `guild_id` IS NULL) OR `guild_id` = CONVERT(@guildId USING utf8mb4) COLLATE utf8mb4_unicode_ci)
  AND `status` = 'active'");

        var queryLike = string.Empty;
        if (!string.IsNullOrWhiteSpace(query))
        {
            queryBuilder.Append(@"
  AND (
      `title` LIKE CONVERT(@queryLike USING utf8mb4) COLLATE utf8mb4_unicode_ci
      OR `content` LIKE CONVERT(@queryLike USING utf8mb4) COLLATE utf8mb4_unicode_ci
      OR `tags` LIKE CONVERT(@queryLike USING utf8mb4) COLLATE utf8mb4_unicode_ci
  )");
            queryLike = $"%{query.Trim()}%";
        }

        queryBuilder.Append(" ORDER BY COALESCE(`updated_at`, `created_at`) DESC, `id` DESC LIMIT @limit");

        var command = new CommandDefinition(
            queryBuilder.ToString(),
            new { channelId, guildId, limit, queryLike },
            cancellationToken: cancellationToken);

        var results = await connection.QueryAsync<ChannelNoteData>(command);
        return results.ToList();
    }

    public async ValueTask<ChannelNoteData?> GetActiveByIdAsync(
        long id,
        string channelId,
        string? guildId,
        CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY = @"
SELECT
    `id` AS Id,
    `guild_id` AS GuildId,
    `channel_id` AS ChannelId,
    `created_by_user_id` AS CreatedByUserId,
    `title` AS Title,
    `content` AS Content,
    `tags` AS Tags,
    `source_message_id` AS SourceMessageId,
    `status` AS Status,
    `created_at` AS CreatedAt,
    `updated_at` AS UpdatedAt
FROM `channel_note`
WHERE `id` = @id
  AND `channel_id` = CONVERT(@channelId USING utf8mb4) COLLATE utf8mb4_unicode_ci
  AND ((@guildId IS NULL AND `guild_id` IS NULL) OR `guild_id` = CONVERT(@guildId USING utf8mb4) COLLATE utf8mb4_unicode_ci)
  AND `status` = 'active'
LIMIT 1";

        var command = new CommandDefinition(
            QUERY,
            new { id, channelId, guildId },
            cancellationToken: cancellationToken);
        return await connection.QueryFirstOrDefaultAsync<ChannelNoteData>(command);
    }

    public async ValueTask<bool> DeleteAsync(
        long id,
        string channelId,
        string? guildId,
        CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY = @"
UPDATE `channel_note`
SET
    `status` = 'deleted',
    `updated_at` = NOW()
WHERE `id` = @id
  AND `channel_id` = CONVERT(@channelId USING utf8mb4) COLLATE utf8mb4_unicode_ci
  AND ((@guildId IS NULL AND `guild_id` IS NULL) OR `guild_id` = CONVERT(@guildId USING utf8mb4) COLLATE utf8mb4_unicode_ci)
  AND `status` = 'active'";

        var command = new CommandDefinition(
            QUERY,
            new { id, channelId, guildId },
            cancellationToken: cancellationToken);
        return await connection.ExecuteAsync(command) > 0;
    }
}
