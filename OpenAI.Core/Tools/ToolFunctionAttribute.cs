namespace OpenAI.Tools;

[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
internal class ToolFunctionAttribute : Attribute
{
    public required string Name { get; init; }

    public string? Description { get; init; }
}
