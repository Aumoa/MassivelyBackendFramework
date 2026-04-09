namespace AI;

[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public class ToolFunctionAttribute : Attribute
{
    public required string Name { get; init; }

    public string? Description { get; init; }
}
