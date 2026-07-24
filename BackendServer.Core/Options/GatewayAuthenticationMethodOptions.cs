using MasterServer.ControlPlane;

namespace BackendServer.Options;

public sealed record GatewayAuthenticationMethodOptions
{
    public string MethodId { get; set; } = string.Empty;

    public GatewayAuthenticationMethodKind Kind { get; set; } = GatewayAuthenticationMethodKind.StaticSecret;

    public string DisplayName { get; set; } = string.Empty;

    public string AuthorityUri { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;

    public string Scope { get; set; } = "openid profile email";

    public string SubjectClaim { get; set; } = "sub";

    public GatewayAuthenticationMethodDefinition ToDefinition()
    {
        return new GatewayAuthenticationMethodDefinition(
            MethodId,
            Kind,
            DisplayName,
            AuthorityUri,
            ClientId,
            Scope,
            SubjectClaim);
    }
}
