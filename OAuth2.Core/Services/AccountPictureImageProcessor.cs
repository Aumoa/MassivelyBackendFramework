using OAuth2.DTO;
using OAuth2.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace OAuth2.Services;

internal static class AccountPictureImageProcessor
{
    private const string JpegContentType = "image/jpeg";
    private const string PngContentType = "image/png";

    public static AccountPictureImageProcessResult Normalize(byte[] bytes, AccountPictureOptions options)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(options);

        if (bytes.Length == 0)
        {
            return AccountPictureImageProcessResult.Failure(AccountPictureError.Empty);
        }

        if (bytes.Length > options.MaxSourceBytes)
        {
            return AccountPictureImageProcessResult.Failure(AccountPictureError.SourceTooLarge);
        }

        if (options.MaxWidth <= 0 || options.MaxHeight <= 0)
        {
            return AccountPictureImageProcessResult.Failure(AccountPictureError.InvalidDimensions);
        }

        try
        {
            var format = Image.DetectFormat(bytes);
            if (!IsSupportedSourceFormat(format))
            {
                return AccountPictureImageProcessResult.Failure(AccountPictureError.UnsupportedFormat);
            }

            using var image = Image.Load(bytes);
            image.Metadata.ExifProfile = null;
            image.Metadata.IccProfile = null;
            image.Metadata.XmpProfile = null;
            image.Mutate(static context => context.AutoOrient());
            image.Mutate(context => context.Resize(new ResizeOptions
            {
                Size = new Size(options.MaxWidth, options.MaxHeight),
                Mode = ResizeMode.Crop,
                Position = AnchorPositionMode.Center
            }));

            var output = SaveAsBoundedJpeg(image, options.MaxBytes);
            return output == null
                ? AccountPictureImageProcessResult.Failure(AccountPictureError.TooLarge)
                : AccountPictureImageProcessResult.Success(output, JpegContentType, options.MaxWidth, options.MaxHeight);
        }
        catch (UnknownImageFormatException)
        {
            return AccountPictureImageProcessResult.Failure(AccountPictureError.UnsupportedFormat);
        }
        catch (InvalidImageContentException)
        {
            return AccountPictureImageProcessResult.Failure(AccountPictureError.UnsupportedFormat);
        }
        catch (NotSupportedException)
        {
            return AccountPictureImageProcessResult.Failure(AccountPictureError.UnsupportedFormat);
        }
    }

    private static bool IsSupportedSourceFormat(IImageFormat format)
    {
        return string.Equals(format.DefaultMimeType, JpegContentType, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(format.DefaultMimeType, PngContentType, StringComparison.OrdinalIgnoreCase);
    }

    private static byte[]? SaveAsBoundedJpeg(Image image, int maxBytes)
    {
        if (maxBytes <= 0)
        {
            return null;
        }

        for (var quality = 92; quality >= 72; quality -= 5)
        {
            using var output = new MemoryStream();
            image.SaveAsJpeg(output, new JpegEncoder { Quality = quality });
            if (output.Length <= maxBytes)
            {
                return output.ToArray();
            }
        }

        return null;
    }
}

internal readonly record struct AccountPictureImageProcessResult(
    byte[]? Bytes,
    string? ContentType,
    int Width,
    int Height,
    AccountPictureError Error)
{
    public bool IsSuccess => Error == AccountPictureError.None && Bytes != null && ContentType != null;

    public static AccountPictureImageProcessResult Success(byte[] bytes, string contentType, int width, int height)
    {
        return new AccountPictureImageProcessResult(bytes, contentType, width, height, AccountPictureError.None);
    }

    public static AccountPictureImageProcessResult Failure(AccountPictureError error)
    {
        return new AccountPictureImageProcessResult(null, null, 0, 0, error);
    }
}
