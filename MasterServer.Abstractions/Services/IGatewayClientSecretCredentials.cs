using System.Threading;
using System.Threading.Tasks;

namespace MasterServer.Services;

public interface IGatewayClientSecretCredentials
{
    ValueTask<GatewayClientSecretCredentialInfo[]> GetCredentialsAsync(CancellationToken cancellationToken = default);

    ValueTask<GatewayClientSecretValidationInfo[]> GetActiveSecretsAsync(CancellationToken cancellationToken = default);

    ValueTask<GatewayClientSecretCredentialCreated> CreateCredentialAsync(
        GatewayClientSecretCredentialInput input,
        CancellationToken cancellationToken = default);

    ValueTask UpdateCredentialAsync(
        long id,
        GatewayClientSecretCredentialInput input,
        CancellationToken cancellationToken = default);

    ValueTask<string> RotateSecretAsync(long id, CancellationToken cancellationToken = default);

    ValueTask RemoveCredentialAsync(long id, CancellationToken cancellationToken = default);
}
