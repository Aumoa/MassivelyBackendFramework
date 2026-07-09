namespace GatewayServer.Options;

public sealed record GatewayAuthenticationOptions
{
    public string PublicBaseUri { get; set; } = string.Empty;

    public string OidcCallbackPath { get; set; } = "/auth/gateway/oidc/callback";

    public int OidcLoginLifetimeSeconds { get; set; } = 600;

    public int OidcLoginRateLimitWindowMilliseconds { get; set; } = 30000;

    public int MaxOidcPendingLogins { get; set; } = 1024;

    public int MaxOidcPendingLoginsPerClient { get; set; } = 4;

    public int MaxOidcLoginCreationsPerWindow { get; set; } = 256;

    public int MaxOidcLoginCreationsPerClientPerWindow { get; set; } = 4;

    public GatewayOidcClientSecretOptions[] OidcClientSecrets { get; set; } = [];
}

public sealed record GatewayOidcClientSecretOptions
{
    public string MethodId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;
}
