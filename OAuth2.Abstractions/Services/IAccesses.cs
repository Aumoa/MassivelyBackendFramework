using OAuth2.DTO;

namespace OAuth2.Services;

public interface IAccesses
{
    ValueTask<Access> WriteAccessAsync(string id, string scope, string clientId, TimeSpan expire, CancellationToken cancellationToken = default);
    ValueTask<string?> VerifyAsync(string accessToken, CancellationToken cancellationToken = default);
    ValueTask<Access?> RefreshAccessAsync(string refreshToken, TimeSpan expire, CancellationToken cancellationToken = default);
}
