namespace MasterServer.ControlPlane;

public enum GatewayAuthenticationMethodKind : byte
{
    StaticSecret = 1,
    OidcAuthorizationCode = 2
}
