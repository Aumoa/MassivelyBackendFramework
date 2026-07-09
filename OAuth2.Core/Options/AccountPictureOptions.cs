namespace OAuth2.Options;

public sealed class AccountPictureOptions
{
    public const int DefaultMaxBytes = 512 * 1024;
    public const int DefaultMaxWidth = 512;
    public const int DefaultMaxHeight = 512;
    public const int DefaultMaxSourceBytes = 8 * 1024 * 1024;
    public const int DefaultMaxSourceWidth = 4096;
    public const int DefaultMaxSourceHeight = 4096;
    public const long DefaultMaxSourcePixels = (long)DefaultMaxSourceWidth * DefaultMaxSourceHeight;

    public int MaxBytes { get; set; } = DefaultMaxBytes;

    public int MaxWidth { get; set; } = DefaultMaxWidth;

    public int MaxHeight { get; set; } = DefaultMaxHeight;

    public int MaxSourceBytes { get; set; } = DefaultMaxSourceBytes;

    public int MaxSourceWidth { get; set; } = DefaultMaxSourceWidth;

    public int MaxSourceHeight { get; set; } = DefaultMaxSourceHeight;

    public long MaxSourcePixels { get; set; } = DefaultMaxSourcePixels;

    public string DefaultPicturePath { get; set; } = Path.Combine("wwwroot", "default-profile.png");

    public TimeSpan RemoteDownloadTimeout { get; set; } = TimeSpan.FromSeconds(5);
}
