namespace GatewayServer.Options;

public sealed record GatewayAuthenticationOptions
{
    public string PublicBaseUri { get; set; } = string.Empty;

    public string OidcCallbackPath { get; set; } = "/auth/gateway/oidc/callback";

    public int OidcLoginLifetimeSeconds { get; set; } = 600;

    public GatewayOidcClientSecretOptions[] OidcClientSecrets { get; set; } = [];
}

public sealed record GatewayOidcClientSecretOptions
{
    public string MethodId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;
}
