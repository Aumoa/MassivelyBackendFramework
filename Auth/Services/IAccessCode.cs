using Auth.DTO;

namespace Auth.Services;

internal interface IAccessCode
{
    ValueTask<AccountRecord?> GetIdAsync(string code, CancellationToken cancellationToken);
}
