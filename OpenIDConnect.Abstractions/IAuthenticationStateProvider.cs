namespace OpenIDConnect;

public interface IAuthenticationStateProvider
{
    string GenerateLoginUri(string redirectUri, string scope);

    ValueTask AcceptAsync(string code, string redirectUri, CancellationToken cancellationToken = default);

    void Clear();
}
