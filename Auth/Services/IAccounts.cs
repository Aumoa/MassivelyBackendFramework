using Auth.DTO;

namespace Auth.Services;

internal interface IAccounts
{
    ValueTask<string> RegisterAccountAsync(string provider, string id, string name, string email, CancellationToken cancellationToken);
    ValueTask<string> GetAccessAsync(string provider, string id, CancellationToken cancellationToken);
    ValueTask<string> RegisterOrGetAccessAsync(string provider, string id, string name, string email, CancellationToken cancellationToken);
    ValueTask<bool> ValidateAccessAsync(string accessJwt, CancellationToken cancellationToken);
}