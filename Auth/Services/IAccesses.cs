namespace Auth.Services;

public interface IAccesses
{
    ValueTask<string> GetAccessAsync(string id, string password, string scope, TimeSpan expireTime, CancellationToken cancellationToken);
}
