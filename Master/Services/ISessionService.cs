namespace Master.Services;

internal interface ISessionService
{
    ValueTask<bool> AddClientAsync(string connectionId, string clientId, CancellationToken cancellationToken);
    ValueTask<bool> RemoveClientAsync(string connectionId, string clientId, CancellationToken cancellationToken);
    ValueTask<string> FindConnectionIdAsync(string clientId, CancellationToken cancellationToken);
}
