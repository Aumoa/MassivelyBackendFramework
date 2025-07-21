namespace Master.Services;

internal interface ISessionService
{
    ValueTask<bool> AddClientAsync(string connectionId, string clientId);
    ValueTask<bool> RemoveClientAsync(string connectionId, string clientId);
}
