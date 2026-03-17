namespace OpenAI.Tools;

[AttributeUsage(AttributeTargets.Parameter, Inherited = false, AllowMultiple = false)]
internal class ToolParameterInfoAttribute : Attribute
{
    public string? Name { get; set; }

    public string? Description { get; set; }
}
