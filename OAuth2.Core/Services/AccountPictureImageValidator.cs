using System.Buffers.Binary;
using OAuth2.DTO;
using OAuth2.Options;

namespace OAuth2.Services;

internal static class AccountPictureImageValidator
{
    private const string PngContentType = "image/png";
    private const string JpegContentType = "image/jpeg";
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];

    public static AccountPictureImageValidationResult Validate(byte[] bytes, AccountPictureOptions options)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(options);

        if (bytes.Length == 0)
        {
            return AccountPictureImageValidationResult.Failure(AccountPictureError.Empty);
        }

        if (bytes.Length > options.MaxBytes)
        {
            return AccountPictureImageValidationResult.Failure(AccountPictureError.TooLarge);
        }

        if (TryReadPngDimensions(bytes, out var width, out var height))
        {
            return ValidateDimensions(PngContentType, width, height, options);
        }

        if (TryReadJpegDimensions(bytes, out width, out height))
        {
            return ValidateDimensions(JpegContentType, width, height, options);
        }

        return AccountPictureImageValidationResult.Failure(AccountPictureError.UnsupportedFormat);
    }

    private static AccountPictureImageValidationResult ValidateDimensions(
        string contentType,
        int width,
        int height,
        AccountPictureOptions options)
    {
        if (width <= 0 ||
            height <= 0 ||
            width > options.MaxWidth ||
            height > options.MaxHeight)
        {
            return AccountPictureImageValidationResult.Failure(AccountPictureError.InvalidDimensions);
        }

        return AccountPictureImageValidationResult.Success(contentType, width, height);
    }

    private static bool TryReadPngDimensions(ReadOnlySpan<byte> bytes, out int width, out int height)
    {
        width = 0;
        height = 0;

        if (bytes.Length < 33 || !bytes[..8].SequenceEqual(PngSignature))
        {
            return false;
        }

        var ihdrLength = BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(8, 4));
        if (ihdrLength != 13 || !bytes.Slice(12, 4).SequenceEqual("IHDR"u8))
        {
            return false;
        }

        width = BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(16, 4));
        height = BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(20, 4));
        return true;
    }

    private static bool TryReadJpegDimensions(ReadOnlySpan<byte> bytes, out int width, out int height)
    {
        width = 0;
        height = 0;

        if (bytes.Length < 4 ||
            bytes[0] != 0xff ||
            bytes[1] != 0xd8 ||
            bytes[^2] != 0xff ||
            bytes[^1] != 0xd9)
        {
            return false;
        }

        var offset = 2;
        while (offset < bytes.Length)
        {
            if (bytes[offset] != 0xff)
            {
                return false;
            }

            while (offset < bytes.Length && bytes[offset] == 0xff)
            {
                offset++;
            }

            if (offset >= bytes.Length)
            {
                return false;
            }

            var marker = bytes[offset++];
            if (marker == 0xd9 || marker == 0xda)
            {
                return false;
            }

            if (marker is 0x01 or >= 0xd0 and <= 0xd7)
            {
                continue;
            }

            if (offset + 2 > bytes.Length)
            {
                return false;
            }

            var segmentLength = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset, 2));
            if (segmentLength < 2 || offset + segmentLength > bytes.Length)
            {
                return false;
            }

            if (IsStartOfFrameMarker(marker))
            {
                if (segmentLength < 7)
                {
                    return false;
                }

                height = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset + 3, 2));
                width = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset + 5, 2));
                return true;
            }

            offset += segmentLength;
        }

        return false;
    }

    private static bool IsStartOfFrameMarker(byte marker)
    {
        return marker is 0xc0 or 0xc1 or 0xc2 or 0xc3 or 0xc5 or 0xc6 or 0xc7 or
            0xc9 or 0xca or 0xcb or 0xcd or 0xce or 0xcf;
    }
}

internal readonly record struct AccountPictureImageValidationResult(
    string? ContentType,
    int Width,
    int Height,
    AccountPictureError Error)
{
    public bool IsSuccess => Error == AccountPictureError.None && ContentType != null;

    public static AccountPictureImageValidationResult Success(string contentType, int width, int height)
    {
        return new AccountPictureImageValidationResult(contentType, width, height, AccountPictureError.None);
    }

    public static AccountPictureImageValidationResult Failure(AccountPictureError error)
    {
        return new AccountPictureImageValidationResult(null, 0, 0, error);
    }
}
