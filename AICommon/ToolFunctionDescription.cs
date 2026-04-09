using System.Text.Json;

namespace AI;

public record ToolFunctionDescription
{
    public enum SimpleType
    {
        String,
        Number,
        Integer,
        Boolean
    }

    public record ParameterInfo
    {
        public required string Name { get; init; }

        public required SimpleType Type { get; init; }

        public required string Description { get; init; }

        public required bool IsRequired { get; init; }

        public required string[]? Enum { get; init; }

        public object? DefaultValue { get; init; }
    }

    public required string Name { get; init; }

    public required Func<object?[]?, object> Invocable { get; init; }

    public required string Description { get; init; }

    public required ParameterInfo[] Parameters { get; init; }

    public required bool HasCancellationTokenParameter { get; init; }

    public object?[] BuildArguments(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        int count = Parameters.Length;
        object?[] args = new object[count + (HasCancellationTokenParameter ? 1 : 0)];
        for (int i = 0; i < count; i++)
        {
            if (arguments.TryGetProperty(Parameters[i].Name, out var value))
            {
                args[i] = Parameters[i].Type switch
                {
                    SimpleType.String => value.GetString(),
                    SimpleType.Number => value.GetDouble(),
                    SimpleType.Integer => value.GetInt32(),
                    SimpleType.Boolean => value.GetBoolean(),
                    _ => null
                };
            }
            else
            {
                args[i] = Parameters[i].DefaultValue;
            }
        }

        if (HasCancellationTokenParameter)
        {
            args[^1] = cancellationToken;
        }

        return args;
    }
}
