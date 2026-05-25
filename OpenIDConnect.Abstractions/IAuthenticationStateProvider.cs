using Microsoft.AspNetCore.Components;

namespace OpenIDConnect;

public interface IAuthenticationStateProvider
{
    string GenerateLoginUri(string redirectUri, string scope);

    ValueTask AcceptAsync(string code, string redirectUri, string? state, CancellationToken cancellationToken = default);

    void Clear();

    void NavigateToLogin(NavigationManager navigation, string redirectRelativeUri, string scope);
}
