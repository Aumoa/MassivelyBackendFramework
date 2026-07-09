using GatewayServer.Protocols;
using MasterServer.ControlPlane;

namespace GatewayServer.Services;

internal interface IGatewayClientAuthenticationChallengeIssuer
{
    ValueTask<GatewayClientAuthenticationMethodChallenge[]> CreateChallengesAsync(
        GatewayAuthenticationMethodDefinition[] methods,
        string backendKind,
        GatewayBackendServerHandle? serverHandle,
        CancellationToken cancellationToken);
}

internal sealed class GatewayClientAuthenticationChallengeIssuer : IGatewayClientAuthenticationChallengeIssuer
{
    public ValueTask<GatewayClientAuthenticationMethodChallenge[]> CreateChallengesAsync(
        GatewayAuthenticationMethodDefinition[] methods,
        string backendKind,
        GatewayBackendServerHandle? serverHandle,
        CancellationToken cancellationToken)
    {
        if (methods == null)
        {
            throw new ArgumentNullException(nameof(methods));
        }

        var challenges = new List<GatewayClientAuthenticationMethodChallenge>();
        foreach (var method in methods)
        {
            if (method.Kind == GatewayAuthenticationMethodKind.StaticSecret)
            {
                challenges.Add(new GatewayClientAuthenticationMethodChallenge(
                    method.MethodId,
                    GatewayClientAuthenticationMethodKind.StaticSecret,
                    method.DisplayName,
                    string.Empty,
                    string.Empty,
                    DateTimeOffset.UnixEpoch));
                continue;
            }

            throw new InvalidOperationException(
                $"Gateway authentication method '{method.MethodId}' of kind '{method.Kind}' is not supported yet.");
        }

        return ValueTask.FromResult(challenges.ToArray());
    }
}
