namespace MinecraftSidecar.Options;

public record RCONOptions
{
    public required string Host { get; init; }

    public required int Port { get; init; }

    public required string Password { get; init; }

    public int MaxRequestBuffer { get; init; } = 100;
}
