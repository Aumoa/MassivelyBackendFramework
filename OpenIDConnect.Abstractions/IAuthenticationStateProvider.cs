using Microsoft.AspNetCore.Components;

using Microsoft.AspNetCore.Http;

namespace OpenIDConnect;

public interface IAuthenticationStateProvider
{
    string GenerateLoginUri(string redirectUri, string scope);

    string GenerateLoginUri(HttpContext httpContext, string redirectUri, string scope);

    ValueTask AcceptAsync(string code, string redirectUri, string? state, CancellationToken cancellationToken = default);

    void Clear();

    void ClearTokenCookies(HttpContext httpContext);

    void NavigateToLogin(NavigationManager navigation, string redirectRelativeUri, string scope);
}
