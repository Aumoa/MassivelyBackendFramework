using Microsoft.Extensions.Hosting;
using static System.Net.WebRequestMethods;

namespace OpenAI.Tools;

internal class StableDiffusion : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    [ToolFunction(Name = "generate_image", Description = "Generates an image based on the given prompt and negative prompt.")]
    public Task<object> GenerateImageAsync(
        [ToolParameterInfo(Description = "The prompt for the image generation.")]
        string prompt,
        [ToolParameterInfo(Name = "negative_prompt", Description = "The negative prompt for the image generation.")]
        string negativePrompt,
        CancellationToken cancellationToken)
    {
        var uri = "https://assets.ayla.r-e.kr/img/profile/liberty.png";
        return Task.FromResult<object>(new
        {
            status = "success",
            image_uri = uri,
            prompt,
            negative_prompt = negativePrompt,
            display_hint = $"![Request Name Topic]({uri})"
        });
    }
}
