namespace MasterServer.Options;

public sealed record MasterAdminConnectionOptions
{
    public string NodeId { get; init; } = string.Empty;

    public string SharedSecret { get; init; } = string.Empty;
}
