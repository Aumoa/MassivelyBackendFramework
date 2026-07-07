using System.Text;
using OAuth2.DTO;
using OAuth2.Options;
using OAuth2.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace OAuth2.Controllers.Tests;

public sealed class AccountPictureImageProcessorTests
{
    [Fact]
    public void Normalize_AcceptsAndResizesPngWithinSourceLimit()
    {
        var result = AccountPictureImageProcessor.Normalize(CreatePng(900, 600), CreateOptions());

        Assert.True(result.IsSuccess);
        Assert.Equal("image/jpeg", result.ContentType);
        Assert.Equal(512, result.Width);
        Assert.Equal(512, result.Height);
        Assert.NotNull(result.Bytes);
        Assert.True(result.Bytes.Length <= CreateOptions().MaxBytes);
    }

    [Fact]
    public void Normalize_AcceptsJpegWithinSourceLimit()
    {
        var result = AccountPictureImageProcessor.Normalize(CreateJpeg(320, 240), CreateOptions());

        Assert.True(result.IsSuccess);
        Assert.Equal("image/jpeg", result.ContentType);
        Assert.Equal(512, result.Width);
        Assert.Equal(512, result.Height);
    }

    [Fact]
    public void Normalize_RejectsSvgMarkup()
    {
        var svg = Encoding.UTF8.GetBytes("""<svg xmlns="http://www.w3.org/2000/svg"><script>alert(1)</script></svg>""");

        var result = AccountPictureImageProcessor.Normalize(svg, CreateOptions());

        Assert.False(result.IsSuccess);
        Assert.Equal(AccountPictureError.UnsupportedFormat, result.Error);
    }

    [Fact]
    public void Normalize_RejectsImageOverSourceByteLimit()
    {
        var options = CreateOptions();
        options.MaxSourceBytes = 4;

        var result = AccountPictureImageProcessor.Normalize([1, 2, 3, 4, 5], options);

        Assert.False(result.IsSuccess);
        Assert.Equal(AccountPictureError.SourceTooLarge, result.Error);
    }

    private static AccountPictureOptions CreateOptions()
    {
        return new AccountPictureOptions
        {
            MaxBytes = 512 * 1024,
            MaxWidth = 512,
            MaxHeight = 512,
            MaxSourceBytes = 8 * 1024 * 1024
        };
    }

    private static byte[] CreatePng(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(32, 128, 192));
        using var output = new MemoryStream();
        image.SaveAsPng(output, new PngEncoder());
        return output.ToArray();
    }

    private static byte[] CreateJpeg(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(192, 128, 32));
        using var output = new MemoryStream();
        image.SaveAsJpeg(output, new JpegEncoder { Quality = 90 });
        return output.ToArray();
    }
}
