using System.Buffers.Binary;
using System.Text;
using OAuth2.DTO;
using OAuth2.Options;
using OAuth2.Services;

namespace OAuth2.Controllers.Tests;

public sealed class AccountPictureImageValidatorTests
{
    [Fact]
    public void Validate_AcceptsPngWithinLimits()
    {
        var result = AccountPictureImageValidator.Validate(CreatePng(128, 96), CreateOptions());

        Assert.True(result.IsSuccess);
        Assert.Equal("image/png", result.ContentType);
        Assert.Equal(128, result.Width);
        Assert.Equal(96, result.Height);
    }

    [Fact]
    public void Validate_AcceptsJpegWithinLimits()
    {
        var result = AccountPictureImageValidator.Validate(CreateJpeg(320, 240), CreateOptions());

        Assert.True(result.IsSuccess);
        Assert.Equal("image/jpeg", result.ContentType);
        Assert.Equal(320, result.Width);
        Assert.Equal(240, result.Height);
    }

    [Fact]
    public void Validate_RejectsSvgMarkup()
    {
        var svg = Encoding.UTF8.GetBytes("""<svg xmlns="http://www.w3.org/2000/svg"><script>alert(1)</script></svg>""");

        var result = AccountPictureImageValidator.Validate(svg, CreateOptions());

        Assert.False(result.IsSuccess);
        Assert.Equal(AccountPictureError.UnsupportedFormat, result.Error);
    }

    [Fact]
    public void Validate_RejectsImageOverDimensionLimit()
    {
        var result = AccountPictureImageValidator.Validate(CreatePng(1025, 1024), CreateOptions());

        Assert.False(result.IsSuccess);
        Assert.Equal(AccountPictureError.InvalidDimensions, result.Error);
    }

    [Fact]
    public void Validate_RejectsImageOverByteLimit()
    {
        var options = CreateOptions();
        options.MaxBytes = 4;

        var result = AccountPictureImageValidator.Validate([1, 2, 3, 4, 5], options);

        Assert.False(result.IsSuccess);
        Assert.Equal(AccountPictureError.TooLarge, result.Error);
    }

    private static AccountPictureOptions CreateOptions()
    {
        return new AccountPictureOptions
        {
            MaxBytes = 512 * 1024,
            MaxWidth = 1024,
            MaxHeight = 1024
        };
    }

    private static byte[] CreatePng(int width, int height)
    {
        var bytes = new byte[33];
        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];
        signature.CopyTo(bytes);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(8, 4), 13);
        "IHDR"u8.CopyTo(bytes.AsSpan(12, 4));
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20, 4), height);
        bytes[24] = 8;
        bytes[25] = 2;
        return bytes;
    }

    private static byte[] CreateJpeg(int width, int height)
    {
        var bytes = new byte[23];
        bytes[0] = 0xff;
        bytes[1] = 0xd8;
        bytes[2] = 0xff;
        bytes[3] = 0xc0;
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(4, 2), 17);
        bytes[6] = 8;
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(7, 2), (ushort)height);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(9, 2), (ushort)width);
        bytes[11] = 3;
        bytes[12] = 1;
        bytes[13] = 0x11;
        bytes[15] = 2;
        bytes[16] = 0x11;
        bytes[18] = 3;
        bytes[19] = 0x11;
        bytes[21] = 0xff;
        bytes[22] = 0xd9;
        return bytes;
    }
}
