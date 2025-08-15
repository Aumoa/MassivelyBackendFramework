namespace OAuth2.Services;

public interface IAccounts
{
    ValueTask<bool> ContainsAsync(string id, CancellationToken cancellationToken);
    ValueTask<bool> AcceptAsync(string id, string password, CancellationToken cancellationToken);
}
