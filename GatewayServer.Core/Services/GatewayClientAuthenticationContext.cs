using GatewayServer.Behaviors;

namespace GatewayServer.Services;

internal enum GatewayClientAuthenticationState
{
    Unauthenticated,
    Authenticated
}

internal sealed class GatewayClientPrincipal
{
    public GatewayClientPrincipal(string subjectId)
    {
        if (string.IsNullOrWhiteSpace(subjectId))
        {
            throw new ArgumentException("Gateway client subject id is required.", nameof(subjectId));
        }

        SubjectId = subjectId.Trim();
    }

    public string SubjectId { get; }
}

internal sealed class GatewayClientAuthenticationContext
{
    private int m_IsAuthenticated;
    private GatewayClientPrincipal? m_Principal;

    public GatewayClientAuthenticationState State => IsAuthenticated
        ? GatewayClientAuthenticationState.Authenticated
        : GatewayClientAuthenticationState.Unauthenticated;

    public bool IsAuthenticated => Volatile.Read(ref m_IsAuthenticated) != 0;

    public GatewayClientPrincipal? Principal => IsAuthenticated ? m_Principal : null;

    public void MarkAuthenticated(GatewayClientPrincipal principal)
    {
        if (principal == null)
        {
            throw new ArgumentNullException(nameof(principal));
        }

        m_Principal = principal;
        Volatile.Write(ref m_IsAuthenticated, 1);
    }
}

internal interface IGatewayClientAuthenticationContextFactory
{
    GatewayClientAuthenticationContext Create(Client client);
}

internal sealed class GatewayClientAuthenticationContextFactory : IGatewayClientAuthenticationContextFactory
{
    public GatewayClientAuthenticationContext Create(Client client)
    {
        if (client == null)
        {
            throw new ArgumentNullException(nameof(client));
        }

        return new GatewayClientAuthenticationContext();
    }
}

internal sealed class GatewayClientTokenValidationResult
{
    private GatewayClientTokenValidationResult(
        bool success,
        GatewayClientPrincipal? principal,
        string errorMessage)
    {
        if (errorMessage == null)
        {
            throw new ArgumentNullException(nameof(errorMessage));
        }

        if (success && principal == null)
        {
            throw new ArgumentException("Successful Gateway client token validation requires a principal.", nameof(principal));
        }

        if (!success && string.IsNullOrWhiteSpace(errorMessage))
        {
            throw new ArgumentException("Rejected Gateway client token validation requires an error message.", nameof(errorMessage));
        }

        Success = success;
        Principal = principal;
        ErrorMessage = errorMessage;
    }

    public bool Success { get; }

    public GatewayClientPrincipal? Principal { get; }

    public string ErrorMessage { get; }

    public static GatewayClientTokenValidationResult Accepted(GatewayClientPrincipal principal)
    {
        return new GatewayClientTokenValidationResult(true, principal, string.Empty);
    }

    public static GatewayClientTokenValidationResult Rejected(string errorMessage)
    {
        return new GatewayClientTokenValidationResult(false, null, errorMessage);
    }
}

internal interface IGatewayClientTokenValidator
{
    ValueTask<GatewayClientTokenValidationResult> ValidateAsync(
        string accessToken,
        CancellationToken cancellationToken);
}

internal sealed class RejectingGatewayClientTokenValidator : IGatewayClientTokenValidator
{
    public ValueTask<GatewayClientTokenValidationResult> ValidateAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(
            GatewayClientTokenValidationResult.Rejected("Gateway client token validator is not configured."));
    }
}
