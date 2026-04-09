using System.Text.Json;

namespace AI;

public record ChatToolCall
{
    public required string Id { get; init; }

    public required string FunctionName { get; init; }

    public required JsonElement Arguments { get; init; }
}
