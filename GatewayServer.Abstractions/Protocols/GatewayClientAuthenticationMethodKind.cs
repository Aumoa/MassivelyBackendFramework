namespace GatewayServer.Protocols;

public enum GatewayClientAuthenticationMethodKind : byte
{
    StaticSecret = 1,
    OidcAuthorizationCode = 2
}
