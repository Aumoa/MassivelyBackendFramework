using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpenAI.Options;

namespace OpenAI.Tools;

internal class StableDiffusion(IOptions<StableDiffusionOptions> options) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private record Txt2ImgRequest
    {
        [JsonPropertyName("prompt")]
        public string Prompt { get; set; } = string.Empty;

        [JsonPropertyName("negative_prompt")]
        public string NegativePrompt { get; set; } = string.Empty;

        [JsonPropertyName("steps")]
        public int Steps { get; set; } = 20;

        [JsonPropertyName("width")]
        public int Width { get; set; } = 512;

        [JsonPropertyName("height")]
        public int Height { get; set; } = 512;

        [JsonPropertyName("batch_size")]
        public int BatchSize = 1;

        [JsonPropertyName("cfg_scale")]
        public double CfgScale { get; set; } = 7.0;
    }

    private record Txt2ImgResponse
    {
        [JsonPropertyName("images")]
        public string[] Images { get; set; } = [];
    }

    [ToolFunction(Name = "generate_image", Description = "Generates an image based on the given prompt and negative prompt.")]
    public async Task<object> GenerateImageAsync(
        [ToolParameterInfo(Description = "The prompt for the image generation.")]
        string prompt,
        [ToolParameterInfo(Name = "negative_prompt", Description = "The negative prompt for the image generation.")]
        string negativePrompt,
        CancellationToken cancellationToken)
    {
        using var client = new HttpClient();
        client.BaseAddress = new Uri(options.Value.Uri);

        var payload = new Txt2ImgRequest
        {
            Prompt = prompt,
            NegativePrompt = negativePrompt
        };

        try
        {
            var response = await client.PostAsJsonAsync("/sdapi/v1/txt2img", payload, cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<Txt2ImgResponse>(cancellationToken: cancellationToken);

            if (result?.Images == null || result.Images.Length == 0)
            {
                return new
                {
                    status = "failure",
                    reason = "parsing error"
                };
            }

            string base64Image = result.Images[0];
            byte[] imageBytes = Convert.FromBase64String(base64Image);

            string fileName = $"{Guid.NewGuid()}.png";
            string filePath = Path.Combine("StableDiffusion", "GeneratedImages", fileName);

            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            await File.WriteAllBytesAsync(filePath, imageBytes, cancellationToken);

            string imageUri = $"/stable-diffusion/generated/{fileName}";

            return new
            {
                status = "success",
                image_uri = imageUri,
                width = 512,
                height = 512,
                display_hint = $"![Generated Image]({imageUri})"
            };
        }
        catch (Exception ex)
        {
            return new { status = "error", message = ex.Message };
        }
    }
}
