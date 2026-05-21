using System.Text.Json.Serialization;

namespace DiscordBot.Services.ImageGeneration;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(ImagePromptProfileProvider.ImagePromptProfile))]
internal partial class ImagePromptProfileJsonContext : JsonSerializerContext;
