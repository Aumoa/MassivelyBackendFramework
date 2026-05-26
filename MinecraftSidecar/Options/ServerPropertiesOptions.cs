namespace MinecraftSidecar.Options;

public record ServerPropertiesOptions
{
    public string FilePath { get; init; } = "/app/server.properties";

    public string? SeedFilePath { get; init; }

    public string EnvironmentVariablePrefix { get; init; } = "MINECRAFT_PROPERTY_";

    public Dictionary<string, string> DefaultProperties { get; init; } = [];
}
