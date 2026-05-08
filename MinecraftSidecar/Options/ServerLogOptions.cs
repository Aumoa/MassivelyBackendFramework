namespace MinecraftSidecar.Options;

public record ServerLogOptions
{
    public string LogFilePath { get; init; } = "/app/logs/latest.log";

    public int InitialLines { get; init; } = 200;

    public int PollingIntervalMs { get; init; } = 100;
}
