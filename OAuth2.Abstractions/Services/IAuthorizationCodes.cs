using OAuth2.DTO;

namespace OAuth2.Services;

public interface IAuthorizationCodes
{
    ValueTask<string> PushAsync(AuthorizationCodeBody body, CancellationToken cancellationToken = default);
    ValueTask<AuthorizationCodeBody?> PopAsync(string code, CancellationToken cancellationToken = default);
}
