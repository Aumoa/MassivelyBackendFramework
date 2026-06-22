namespace MasterServer.Services;

public sealed class GatewayClientSecretCredentialCreated
{
    public GatewayClientSecretCredentialCreated(
        GatewayClientSecretCredentialInfo credential,
        string accessToken)
    {
        Credential = credential ?? throw new System.ArgumentNullException(nameof(credential));
        AccessToken = accessToken ?? throw new System.ArgumentNullException(nameof(accessToken));
    }

    public GatewayClientSecretCredentialInfo Credential { get; }

    public string AccessToken { get; }
}
