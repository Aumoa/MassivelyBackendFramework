using System.Text.Json.Serialization;
using OpenAI.Tools;

namespace OpenAI.Ollama;

internal record OllamaFaceParameters
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "object";

    [JsonPropertyName("properties")]
    public required Dictionary<string, OllamaProperty> Properties { get; init; }

    [JsonPropertyName("required")]
    public string[]? Required { get; init; }

    public static OllamaFaceParameters CreateFrom(ToolFunctionDescription.ParameterInfo[] parameterInfos)
    {
        var properties = parameterInfos.ToDictionary(
            pi => pi.Name,
            pi => new OllamaProperty
            {
                Type = pi.Type switch
                {
                    ToolFunctionDescription.SimpleType.String => "string",
                    ToolFunctionDescription.SimpleType.Number => "number",
                    ToolFunctionDescription.SimpleType.Boolean => "boolean",
                    _ => throw new InvalidOperationException($"Unsupported parameter type: {pi.Type}")
                },
                Enum = pi.Enum
            });
        var required = parameterInfos.Where(pi => pi.IsRequired).Select(pi => pi.Name).ToArray();
        return new OllamaFaceParameters
        {
            Properties = properties,
            Required = required.Length > 0 ? required : null
        };
    }
}
