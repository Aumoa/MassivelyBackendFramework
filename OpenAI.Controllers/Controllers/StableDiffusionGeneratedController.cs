using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace OpenAI.Controllers;

[ApiController]
[Route("stable-diffusion/generated")]
[Authorize]
public class StableDiffusionGeneratedController : ControllerBase
{
    private const string GeneratedImagesRoot = "StableDiffusion";
    private const string GeneratedImagesDirectory = "GeneratedImages";

    private static readonly Regex s_GeneratedImageNamePattern = new(
        @"^(?:\d+_)?[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\.png$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    [HttpGet("{imageName}")]
    public IActionResult Get([FromRoute] string imageName)
    {
        if (!TryGetGeneratedImagePath(Directory.GetCurrentDirectory(), imageName, out var filePath))
        {
            return NotFound();
        }

        if (!System.IO.File.Exists(filePath))
        {
            return NotFound();
        }

        return PhysicalFile(filePath, "image/png");
    }

    internal static bool TryGetGeneratedImagePath(
        string contentRootPath,
        string imageName,
        [NotNullWhen(true)] out string? filePath)
    {
        filePath = null;
        if (string.IsNullOrWhiteSpace(contentRootPath) ||
            string.IsNullOrWhiteSpace(imageName) ||
            !s_GeneratedImageNamePattern.IsMatch(imageName))
        {
            return false;
        }

        var imageRoot = Path.GetFullPath(Path.Combine(contentRootPath, GeneratedImagesRoot, GeneratedImagesDirectory));
        var candidate = Path.GetFullPath(Path.Combine(imageRoot, imageName));
        var imageRootWithSeparator = Path.EndsInDirectorySeparator(imageRoot)
            ? imageRoot
            : imageRoot + Path.DirectorySeparatorChar;

        if (!candidate.StartsWith(imageRootWithSeparator, GetPathComparison()))
        {
            return false;
        }

        filePath = candidate;
        return true;
    }

    private static StringComparison GetPathComparison()
    {
        return OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
    }
}
