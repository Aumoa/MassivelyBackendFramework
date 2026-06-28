namespace OAuth2;

public sealed class ClientIdAlreadyExistsException : Exception
{
    public ClientIdAlreadyExistsException(string clientId)
        : base($"Client ID '{clientId}' already exists.")
    {
        ClientId = clientId;
    }

    public string ClientId { get; }
}
