namespace AI;

[AttributeUsage(AttributeTargets.Parameter, Inherited = false, AllowMultiple = false)]
public class ToolParameterInfoAttribute : Attribute
{
    public string? Name { get; set; }

    public string? Description { get; set; }
}
