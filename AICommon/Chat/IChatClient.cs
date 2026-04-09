namespace AI;

public interface IChatClient
{
    IAsyncEnumerable<ChatResponseChunk> ChatAsync(
        IReadOnlyList<ChatMessage> messages,
        ChatCompletionOptions options,
        IReadOnlyList<ToolFunctionDescription>? tools = null,
        CancellationToken cancellationToken = default);

    Task<string> GenerateAsync(
        string prompt,
        ChatCompletionOptions options,
        string? system = null,
        CancellationToken cancellationToken = default);
}
