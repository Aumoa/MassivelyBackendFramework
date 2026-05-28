using OAuth2.DTO;

namespace OAuth2.Services;

public interface IAuthorizationCodes
{
    ValueTask<string> PushAsync(AuthorizationCodeBody body, CancellationToken cancellationToken = default);
    ValueTask<AuthorizationCodeBody?> PopAsync(string code, CancellationToken cancellationToken = default);
    ValueTask<string?> GetIssuedAccessTokenAsync(string code, CancellationToken cancellationToken = default);
    ValueTask StoreIssuedAccessTokenAsync(string code, string accessToken, CancellationToken cancellationToken = default);
}
