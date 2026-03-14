using Dapper;
using Microsoft.Extensions.Options;
using OpenAI.Options;

namespace OpenAI.Services;

internal class MySqlChatRepository(IOptions<MySqlOptions> options) : MySqlDbContext(options.Value), IChatRepository
{
    public async ValueTask<IReadOnlyList<ChatSessionData>> GetSessionsAsync(string userId, CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY = "SELECT `id`, `user_id` AS `UserId`, `topic`, `created_at` AS `CreatedAt` FROM `chat_session` WHERE `user_id` = @userId ORDER BY `created_at` DESC";
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

        const string QUERY = "SELECT `id`, `session_id` AS `SessionId`, `is_user` AS `IsUser`, `content`, `created_at` AS `CreatedAt` FROM `chat_message` WHERE `session_id` = @sessionId ORDER BY `created_at` ASC, `id` ASC";
        var command = new CommandDefinition(QUERY, new { sessionId }, cancellationToken: cancellationToken);
        var results = await connection.QueryAsync<ChatMessageData>(command);
        return [.. results];
    }

    public async ValueTask AddMessageAsync(string sessionId, bool isUser, string content, CancellationToken cancellationToken = default)
    {
        using var connection = GetConnection();

        const string QUERY = "INSERT INTO `chat_message` (`session_id`, `is_user`, `content`) VALUES(@sessionId, @isUser, @content)";
        var command = new CommandDefinition(QUERY, new { sessionId, isUser = isUser ? 1 : 0, content }, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }
}
