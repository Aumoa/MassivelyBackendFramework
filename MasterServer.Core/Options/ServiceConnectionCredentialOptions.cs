namespace MasterServer.Options;

public sealed record ServiceConnectionCredentialOptions
{
    public bool Enabled { get; init; }

    public string Server { get; init; } = "localhost";

    public int Port { get; init; } = 3306;

    public string Database { get; init; } = "MassivelyBackendFramework__MasterServer";

    public string User { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;

    public string DataProtectionApplicationName { get; init; } = "MasterAdmin";

    public int RevalidationIntervalMilliseconds { get; init; } = 10000;

    public string ConnectionString => $"Server={Server};Port={Port};Database={Database};Uid={User};Pwd={Password};";
}
