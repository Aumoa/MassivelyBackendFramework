namespace MasterServer.Services;

public sealed class ServiceConnectionCredentialCreated
{
    public ServiceConnectionCredentialCreated(
        ServiceConnectionCredentialInfo credential,
        string sharedSecret)
    {
        Credential = credential;
        SharedSecret = sharedSecret;
    }

    public ServiceConnectionCredentialInfo Credential { get; }

    public string SharedSecret { get; }
}
