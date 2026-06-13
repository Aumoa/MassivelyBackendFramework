using OpenAI.Controllers;

namespace OpenAI.Controllers.Tests.Controllers;

public sealed class StableDiffusionGeneratedControllerTests
{
    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000.png")]
    [InlineData("12_00000000-0000-0000-0000-000000000000.png")]
    public void TryGetGeneratedImagePath_AcceptsGeneratedImageNames(string imageName)
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));

        var result = StableDiffusionGeneratedController.TryGetGeneratedImagePath(root, imageName, out var filePath);

        Assert.True(result);
        Assert.NotNull(filePath);
        Assert.StartsWith(Path.Combine(root, "StableDiffusion", "GeneratedImages"), filePath);
        Assert.EndsWith(imageName, filePath);
    }

    [Theory]
    [InlineData("../appsettings.json")]
    [InlineData("..\\appsettings.json")]
    [InlineData("nested/00000000-0000-0000-0000-000000000000.png")]
    [InlineData("nested\\00000000-0000-0000-0000-000000000000.png")]
    [InlineData("not-a-guid.png")]
    [InlineData("00000000-0000-0000-0000-000000000000.jpg")]
    public void TryGetGeneratedImagePath_RejectsTraversalAndUnexpectedNames(string imageName)
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));

        var result = StableDiffusionGeneratedController.TryGetGeneratedImagePath(root, imageName, out var filePath);

        Assert.False(result);
        Assert.Null(filePath);
    }
}
