using Scripting.DTO;

namespace Auth.Services;

internal interface IAccounts
{
    ValueTask<bool> ContainsAsync(string id, CancellationToken cancellationToken);
    ValueTask<ResponseCode> RegisterAsync(string id, string password, string email, CancellationToken cancellationToken);
}