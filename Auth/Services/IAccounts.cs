using Scripting.DTO;

namespace Auth.Services;

internal interface IAccounts : ISelfProviderAccessCode
{
    ValueTask<bool> ContainsAsync(string id, CancellationToken cancellationToken);
    ValueTask<ResponseCode> RegisterAsync(string id, string password, string email, CancellationToken cancellationToken);
}