namespace SecretGate.Options;

public sealed record MySqlOptions
{
    public string Server { get; init; } = "localhost";

    public int Port { get; init; } = 3306;

    public string Database { get; init; } = "MassivelyBackendFramework__SecretGate";

    public required string User { get; init; }

    public required string Password { get; init; }

    public string SslMode { get; init; } = "Preferred";

    public bool AllowPublicKeyRetrieval { get; init; }

    public string ConnectionString => $"Server={Server};Port={Port};Database={Database};Uid={User};Pwd={Password};SslMode={SslMode};AllowPublicKeyRetrieval={AllowPublicKeyRetrieval};";
}
