using Dapper;
using DiscordBot.Options;
using Microsoft.Extensions.Options;

namespace DiscordBot.Repositories;

internal sealed class MySqlAllowedChannelRequestRepository(IOptions<MySqlOptions> options)
    : MySqlDbContext(options.Value), IAllowedChannelRequestRepository
{
    public async ValueTask<AllowedChannelRequestData?> GetByTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY = @"
SELECT
    `id` AS Id,
    `token` AS Token,
    `guild_id` AS GuildId,
    `channel_id` AS ChannelId,
    `guild_name` AS GuildName,
    `channel_name` AS ChannelName,
    `requester_id` AS RequesterId,
    `requester_name` AS RequesterName,
    `message_id` AS MessageId,
    `status` AS Status,
    `approved_by` AS ApprovedBy,
    `approved_at` AS ApprovedAt,
    `created_at` AS CreatedAt,
    `expires_at` AS ExpiresAt
FROM `allowed_channel_request`
WHERE `token` = @token
LIMIT 1";

        var command = new CommandDefinition(QUERY, new { token }, cancellationToken: cancellationToken);
        return await connection.QueryFirstOrDefaultAsync<AllowedChannelRequestData>(command);
    }

    public async ValueTask<AllowedChannelRequestData?> GetPendingByChannelIdAsync(
        string channelId,
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY = @"
SELECT
    `id` AS Id,
    `token` AS Token,
    `guild_id` AS GuildId,
    `channel_id` AS ChannelId,
    `guild_name` AS GuildName,
    `channel_name` AS ChannelName,
    `requester_id` AS RequesterId,
    `requester_name` AS RequesterName,
    `message_id` AS MessageId,
    `status` AS Status,
    `approved_by` AS ApprovedBy,
    `approved_at` AS ApprovedAt,
    `created_at` AS CreatedAt,
    `expires_at` AS ExpiresAt
FROM `allowed_channel_request`
WHERE `channel_id` = @channelId
  AND `status` = 'pending'
  AND `expires_at` > @now
ORDER BY `created_at` DESC
LIMIT 1";

        var command = new CommandDefinition(
            QUERY,
            new { channelId, now },
            cancellationToken: cancellationToken);
        return await connection.QueryFirstOrDefaultAsync<AllowedChannelRequestData>(command);
    }

    public async ValueTask AddAsync(AllowedChannelRequestInput request, CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY = @"
INSERT INTO `allowed_channel_request`
    (`token`, `guild_id`, `channel_id`, `guild_name`, `channel_name`, `requester_id`, `requester_name`, `message_id`, `expires_at`)
VALUES
    (@Token, @GuildId, @ChannelId, @GuildName, @ChannelName, @RequesterId, @RequesterName, @MessageId, @ExpiresAt)";

        var command = new CommandDefinition(QUERY, request, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }

    public async ValueTask MarkApprovedAsync(long id, string? approvedBy, CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY = @"
UPDATE `allowed_channel_request`
SET
    `status` = 'approved',
    `approved_by` = @approvedBy,
    `approved_at` = NOW()
WHERE `id` = @id
  AND `status` = 'pending'";

        var command = new CommandDefinition(
            QUERY,
            new { id, approvedBy },
            cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }
}
