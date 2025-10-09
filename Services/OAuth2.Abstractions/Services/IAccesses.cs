using OAuth2.DTO;

namespace OAuth2.Services;

public interface IAccesses
{
    ValueTask<Access> WriteAccessAsync(string id, TimeSpan expire, CancellationToken cancellationToken = default);
    ValueTask<bool> VerifyAsync(string accessToken, CancellationToken cancellationToken = default);
    ValueTask<string?> RefreshAccessAsync(string refreshToken, TimeSpan expire, CancellationToken cancellationToken = default);
}
