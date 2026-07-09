namespace GatewayServer.Services;

internal interface IGatewayClientStaticTokenValidator
{
    ValueTask<GatewayClientTokenValidationResult> ValidateStaticTokenAsync(
        string accessToken,
        CancellationToken cancellationToken);
}

internal interface IGatewayClientOidcCompletionTokenValidator
{
    bool IsOidcCompletionToken(string accessToken);

    ValueTask<GatewayClientTokenValidationResult> ValidateOidcCompletionTokenAsync(
        string accessToken,
        CancellationToken cancellationToken);
}

internal sealed class GatewayClientTokenValidator(
    IGatewayClientStaticTokenValidator staticTokens,
    IGatewayClientOidcCompletionTokenValidator oidcCompletionTokens) : IGatewayClientTokenValidator
{
    public ValueTask<GatewayClientTokenValidationResult> ValidateAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        return oidcCompletionTokens.IsOidcCompletionToken(accessToken)
            ? oidcCompletionTokens.ValidateOidcCompletionTokenAsync(accessToken, cancellationToken)
            : staticTokens.ValidateStaticTokenAsync(accessToken, cancellationToken);
    }
}
