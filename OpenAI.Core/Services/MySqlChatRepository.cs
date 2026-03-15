using System.Data;
using Dapper;
using Microsoft.Extensions.Options;
using OpenAI.Options;

namespace OpenAI.Services;

internal class MySqlChatRepository(IOptions<MySqlOptions> options) : MySqlDbContext(options.Value), IChatRepository
{
    public async ValueTask<IReadOnlyList<ChatSessionData>> GetSessionsAsync(string userId, CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY = "SELECT `id`, `user_id` AS `UserId`, `topic`, `created_at` AS `CreatedAt` FROM `chat_session` WHERE `user_id` = @userId AND `removed_at` IS NULL ORDER BY `created_at` DESC";
        var command = new CommandDefinition(QUERY, new { userId }, cancellationToken: cancellationToken);
        var results = await connection.QueryAsync<ChatSessionData>(command);
        return [.. results];
    }

    public async ValueTask<string> CreateSessionAsync(string userId, string topic, CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        string id = Guid.NewGuid().ToString("N");
        const string QUERY = "INSERT INTO `chat_session` (`id`, `user_id`, `topic`) VALUES(@id, @userId, @topic)";
        var command = new CommandDefinition(QUERY, new { id, userId, topic }, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
        return id;
    }

    public async ValueTask<IReadOnlyList<ChatMessageData>> GetMessagesAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY = "SELECT `id`, `session_id` AS `SessionId`, `role` AS `Role`, `content`, `created_at` AS `CreatedAt` FROM `chat_message` WHERE `session_id` = @sessionId ORDER BY `created_at` ASC, `id` ASC";
        var command = new CommandDefinition(QUERY, new { sessionId }, cancellationToken: cancellationToken);
        var results = await connection.QueryAsync<ChatMessageData>(command);
        return [.. results];
    }

    public async ValueTask<long> AddMessageAsync(string sessionId, MessageRole role, string content, CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();
        await connection.OpenAsync(cancellationToken);

        const string QUERY = "INSERT INTO `chat_message` (`session_id`, `role`, `content`) VALUES(@sessionId, @role, @content)";
        var command = new CommandDefinition(QUERY, new { sessionId, role = (int)role, content }, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition("SELECT LAST_INSERT_ID()", cancellationToken: cancellationToken));
    }

    public async ValueTask<long> ReplaceWithSummaryAsync(string sessionId, IEnumerable<long> messageIds, string summaryContent, CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();
        await connection.OpenAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        var ids = messageIds.ToArray();
        if (ids.Length > 0)
        {
            const string DELETE_QUERY = "DELETE FROM `chat_message` WHERE `id` IN @ids";
            var deleteCommand = new CommandDefinition(DELETE_QUERY, new { ids }, tx, cancellationToken: cancellationToken);
            await connection.ExecuteAsync(deleteCommand);
        }

        const string INSERT_QUERY = "INSERT INTO `chat_message` (`session_id`, `role`, `content`) VALUES(@sessionId, @role, @content)";
        var insertCommand = new CommandDefinition(INSERT_QUERY, new { sessionId, role = (int)MessageRole.Summary, content = summaryContent }, tx, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(insertCommand);

        long newId = await connection.ExecuteScalarAsync<long>(new CommandDefinition("SELECT LAST_INSERT_ID()", tx, cancellationToken: cancellationToken));
        await tx.CommitAsync(cancellationToken);
        return newId;
    }

    public async ValueTask RemoveSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY = "UPDATE `chat_session` SET `removed_at` = NOW() WHERE `id` = @sessionId AND `removed_at` IS NULL";
        var command = new CommandDefinition(QUERY, new { sessionId }, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }
}
