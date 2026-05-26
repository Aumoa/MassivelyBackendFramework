using OAuth2.DTO;

namespace OAuth2.Services;

public interface IAccesses
{
    ValueTask<Access> WriteAccessAsync(string id, string sub, string scope, string clientId, TimeSpan expire, TimeSpan refreshTokenExpire, CancellationToken cancellationToken = default, long? authTime = null);
    ValueTask<Access?> VerifyAsync(string accessToken, CancellationToken cancellationToken = default);
    ValueTask<Access?> VerifyRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default);
    ValueTask<Access?> RefreshAccessAsync(string refreshToken, TimeSpan expire, TimeSpan refreshTokenExpire, CancellationToken cancellationToken = default);
    ValueTask<Access?> RefreshAccessAsync(string refreshToken, string clientId, TimeSpan expire, TimeSpan refreshTokenExpire, CancellationToken cancellationToken = default);
    ValueTask RevokeAsync(string accessToken, CancellationToken cancellationToken = default);
    ValueTask InvalidateAllTokensAsync(string sub, CancellationToken cancellationToken = default);
    ValueTask InvalidateClientTokensAsync(string sub, string clientId, CancellationToken cancellationToken = default);
}
