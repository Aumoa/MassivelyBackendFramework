namespace OAuth2.Services;

public interface IAccesses
{
    ValueTask<string> GetAccessAsync(string id, string scope, TimeSpan expireTime, CancellationToken cancellationToken);
    ValueTask<string[]> QueryRolesAsync(string accessToken, CancellationToken cancellationToken);
}
