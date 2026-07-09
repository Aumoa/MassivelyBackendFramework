using System;
using PacketCore;

namespace MasterServer.ControlPlane;

public sealed class GatewayAuthenticationMethodDefinition
{
    public const int MaxMethodIdLength = 128;
    public const int MaxDisplayNameLength = 256;
    public const int MaxAuthorityUriLength = 2048;
    public const int MaxClientIdLength = 256;
    public const int MaxScopeLength = 1024;
    public const int MaxSubjectClaimLength = 128;

    public GatewayAuthenticationMethodDefinition(
        string methodId,
        GatewayAuthenticationMethodKind kind,
        string displayName,
        string authorityUri,
        string clientId,
        string scope,
        string subjectClaim)
    {
        MethodId = NormalizeRequired(methodId, MaxMethodIdLength, nameof(methodId));
        if (!Enum.IsDefined(typeof(GatewayAuthenticationMethodKind), kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        Kind = kind;
        DisplayName = NormalizeRequired(displayName, MaxDisplayNameLength, nameof(displayName));
        AuthorityUri = NormalizeOptional(authorityUri, MaxAuthorityUriLength, nameof(authorityUri));
        ClientId = NormalizeOptional(clientId, MaxClientIdLength, nameof(clientId));
        Scope = NormalizeOptional(scope, MaxScopeLength, nameof(scope));
        SubjectClaim = NormalizeOptional(subjectClaim, MaxSubjectClaimLength, nameof(subjectClaim));

        if (Kind == GatewayAuthenticationMethodKind.OidcAuthorizationCode)
        {
            if (string.IsNullOrWhiteSpace(AuthorityUri))
            {
                throw new ArgumentException("OIDC authentication methods require an authority URI.", nameof(authorityUri));
            }

            if (!Uri.TryCreate(AuthorityUri, UriKind.Absolute, out var authority) ||
                authority.Scheme is not ("http" or "https"))
            {
                throw new ArgumentException("OIDC authority URI must be an absolute HTTP or HTTPS URI.", nameof(authorityUri));
            }

            if (string.IsNullOrWhiteSpace(ClientId))
            {
                throw new ArgumentException("OIDC authentication methods require a client id.", nameof(clientId));
            }

            if (string.IsNullOrWhiteSpace(Scope))
            {
                throw new ArgumentException("OIDC authentication methods require a scope.", nameof(scope));
            }

            if (string.IsNullOrWhiteSpace(SubjectClaim))
            {
                SubjectClaim = "sub";
            }
        }
        else
        {
            SubjectClaim = string.IsNullOrWhiteSpace(SubjectClaim)
                ? string.Empty
                : SubjectClaim;
        }
    }

    public string MethodId { get; }

    public GatewayAuthenticationMethodKind Kind { get; }

    public string DisplayName { get; }

    public string AuthorityUri { get; }

    public string ClientId { get; }

    public string Scope { get; }

    public string SubjectClaim { get; private set; }

    public static GatewayAuthenticationMethodDefinition StaticSecret(string methodId, string displayName)
    {
        return new GatewayAuthenticationMethodDefinition(
            methodId,
            GatewayAuthenticationMethodKind.StaticSecret,
            displayName,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty);
    }

    public static GatewayAuthenticationMethodDefinition OidcAuthorizationCode(
        string methodId,
        string displayName,
        string authorityUri,
        string clientId,
        string scope,
        string subjectClaim = "sub")
    {
        return new GatewayAuthenticationMethodDefinition(
            methodId,
            GatewayAuthenticationMethodKind.OidcAuthorizationCode,
            displayName,
            authorityUri,
            clientId,
            scope,
            subjectClaim);
    }

    public static IPacketCodec<GatewayAuthenticationMethodDefinition> Codec { get; } = new GatewayAuthenticationMethodDefinitionCodec();

    private static string NormalizeRequired(string value, int maxLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value is required.", parameterName);
        }

        return Normalize(value, maxLength, parameterName);
    }

    private static string NormalizeOptional(string? value, int maxLength, string parameterName)
    {
        if (value == null)
        {
            return string.Empty;
        }

        return Normalize(value, maxLength, parameterName);
    }

    private static string Normalize(string value, int maxLength, string parameterName)
    {
        var normalized = value.Trim();
        if (normalized.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                $"Value must be {maxLength} characters or fewer.");
        }

        return normalized;
    }

    private sealed class GatewayAuthenticationMethodDefinitionCodec : IPacketCodec<GatewayAuthenticationMethodDefinition>
    {
        public int GetPayloadSize(GatewayAuthenticationMethodDefinition value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            return PacketWriter.GetStringSize(value.MethodId) +
                   sizeof(byte) +
                   PacketWriter.GetStringSize(value.DisplayName) +
                   PacketWriter.GetStringSize(value.AuthorityUri) +
                   PacketWriter.GetStringSize(value.ClientId) +
                   PacketWriter.GetStringSize(value.Scope) +
                   PacketWriter.GetStringSize(value.SubjectClaim);
        }

        public void Encode(GatewayAuthenticationMethodDefinition value, ref PacketWriter writer)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            writer.WriteString(value.MethodId);
            writer.WriteByte((byte)value.Kind);
            writer.WriteString(value.DisplayName);
            writer.WriteString(value.AuthorityUri);
            writer.WriteString(value.ClientId);
            writer.WriteString(value.Scope);
            writer.WriteString(value.SubjectClaim);
        }

        public GatewayAuthenticationMethodDefinition Decode(ref PacketReader reader)
        {
            return new GatewayAuthenticationMethodDefinition(
                reader.ReadString(),
                (GatewayAuthenticationMethodKind)reader.ReadByte(),
                reader.ReadString(),
                reader.ReadString(),
                reader.ReadString(),
                reader.ReadString(),
                reader.ReadString());
        }
    }
}
