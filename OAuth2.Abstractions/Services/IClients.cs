using OAuth2.DTO;

namespace OAuth2.Services;

public interface IClients
{
    ValueTask<string> AddClientAsync(string name, string ownerId, string[] redirectUris, CancellationToken cancellationToken = default);
    ValueTask<ClientInfo?> GetClientAsync(string clientId, CancellationToken cancellationToken = default);
    ValueTask<ClientInfo[]> GetClientsAsync(string ownerId, CancellationToken cancellationToken = default);
    ValueTask<string> NewClientSecretAsync(string clientId, CancellationToken cancellationToken = default);
    ValueTask RemoveClientAsync(string clientId, CancellationToken cancellationToken = default);
}
