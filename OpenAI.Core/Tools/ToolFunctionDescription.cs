namespace OpenAI.Tools;

internal record ToolFunctionDescription
{
    public enum SimpleType
    {
        String,
        Number,
        Boolean
    }

    public record ParameterInfo
    {
        public required string Name { get; init; }

        public required SimpleType Type { get; init; }

        public required string Description { get; init; }

        public required bool IsRequired { get; init; }

        public required string[]? Enum { get; init; }
    }

    public required string Name { get; init; }

    public required Func<object?[]?, IAsyncEnumerable<ChunkedResponse>> Invocable { get; init; }

    public required string Description { get; init; }

    public required ParameterInfo[] Parameters { get; init; }

    public required bool HasCancellationTokenParameter { get; init; }
}
