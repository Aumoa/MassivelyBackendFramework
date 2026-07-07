namespace OAuth2.DTO;

public enum AccountPictureError
{
    None,
    Empty,
    TooLarge,
    UnsupportedFormat,
    InvalidDimensions,
    InvalidUrl,
    DownloadFailed,
    DefaultUnavailable
}
