using OAuth2.DTO;

namespace OAuth2.Services;

public interface IAccountPictures
{
    ValueTask<AccountPicture?> GetPictureAsync(string accountId, CancellationToken cancellationToken = default);
    ValueTask<AccountPicture> GetOrCreateDefaultPictureAsync(string accountId, CancellationToken cancellationToken = default);
    ValueTask EnsurePictureAsync(string accountId, string? legacyPictureUrl = null, CancellationToken cancellationToken = default);
    ValueTask<AccountPictureUpdateResult> SetPictureAsync(string accountId, Stream image, CancellationToken cancellationToken = default);
}
