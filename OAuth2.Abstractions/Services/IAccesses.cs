using OAuth2.DTO;

namespace OAuth2.Services;

public interface IAccesses
{
    ValueTask<Access> WriteAccessAsync(string id, string sub, string scope, string clientId, TimeSpan expire, CancellationToken cancellationToken = default);
    ValueTask<Access?> VerifyAsync(string accessToken, CancellationToken cancellationToken = default);
    ValueTask<Access?> VerifyRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default);
    ValueTask<Access?> RefreshAccessAsync(string refreshToken, TimeSpan expire, CancellationToken cancellationToken = default);
}
