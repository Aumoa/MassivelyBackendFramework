using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace OpenAI.Controllers;

[ApiController]
[Route("stable-diffusion/generated")]
[Authorize]
public class StableDiffusionGeneratedController : ControllerBase
{
    [HttpGet("{imageName}")]
    public IActionResult Get([FromRoute] string imageName)
    {
        var filePath = Path.Combine("StableDiffusion", "GeneratedImages", imageName);
        if (!System.IO.File.Exists(filePath))
        {
            return NotFound();
        }
        var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        return File(fileStream, "image/png");
    }
}
