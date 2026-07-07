namespace OAuth2.DTO;

public enum AccountPictureError
{
    None,
    Empty,
    TooLarge,
    SourceTooLarge,
    UnsupportedFormat,
    InvalidDimensions,
    InvalidUrl,
    DownloadFailed,
    DefaultUnavailable
}
