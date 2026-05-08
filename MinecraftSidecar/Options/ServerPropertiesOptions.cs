namespace MinecraftSidecar.Options;

public record ServerPropertiesOptions
{
    public string FilePath { get; init; } = "/app/server.properties";
}
