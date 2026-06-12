namespace MasterServer.Services;

public interface IServiceConnectionCredentials
{
    ValueTask<ServiceConnectionCredentialInfo[]> GetCredentialsAsync(CancellationToken cancellationToken = default);

    ValueTask<ServiceConnectionCredentialCreated> CreateCredentialAsync(
        ServiceConnectionCredentialInput input,
        CancellationToken cancellationToken = default);

    ValueTask UpdateCredentialAsync(
        long id,
        ServiceConnectionCredentialInput input,
        CancellationToken cancellationToken = default);

    ValueTask<string> RotateSecretAsync(long id, CancellationToken cancellationToken = default);

    ValueTask RemoveCredentialAsync(long id, CancellationToken cancellationToken = default);
}
