namespace OAuth2.Options;

public sealed class AccountPictureOptions
{
    public const int DefaultMaxBytes = 512 * 1024;
    public const int DefaultMaxWidth = 512;
    public const int DefaultMaxHeight = 512;

    public int MaxBytes { get; set; } = DefaultMaxBytes;

    public int MaxWidth { get; set; } = DefaultMaxWidth;

    public int MaxHeight { get; set; } = DefaultMaxHeight;

    public string DefaultPicturePath { get; set; } = Path.Combine("wwwroot", "default-profile.png");

    public TimeSpan RemoteDownloadTimeout { get; set; } = TimeSpan.FromSeconds(5);
}
