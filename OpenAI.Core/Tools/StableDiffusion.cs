using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using AI;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace OpenAI.Tools;

internal class StableDiffusion(ILogger<StableDiffusion> logger, HttpClient http) : IHostedService
{
    private const int ImageWidth = 512;
    private const int ImageHeight = 512;

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
        public int Width { get; set; } = ImageWidth;

        [JsonPropertyName("height")]
        public int Height { get; set; } = ImageHeight;

        [JsonPropertyName("batch_size")]
        public int BatchSize = 1;

        [JsonPropertyName("cfg_scale")]
        public double CfgScale { get; set; } = 7.0;

        [JsonPropertyName("sampler_name")]
        public string? SamplerName { get; set; }

        [JsonPropertyName("scheduler")]
        public string? Scheduler { get; set; }

        [JsonPropertyName("enable_hr")]
        public bool EnableHires { get; set; }

        [JsonPropertyName("hr_scale")]
        public double HiresScale { get; set; } = 2;

        [JsonPropertyName("hr_upscaler")]
        public string? HiresUpscaler { get; set; }

        [JsonPropertyName("send_images")]
        public bool SendImages { get; set; } = true;

        [JsonPropertyName("save_images")]
        public bool SaveImages { get; set; } = false;

        [JsonPropertyName("denoising_strength")]
        public double DesnoisingStrength { get; set; } = 0;
    }

    private record Txt2ImgResponse
    {
        [JsonPropertyName("images")]
        public string[] Images { get; set; } = [];
    }

    private record ProgressState
    {
        [JsonPropertyName("sampling_step")]
        public int SamplingStep { get; set; }

        [JsonPropertyName("sampling_steps")]
        public int SamplingSteps { get; set; }
    }

    private record ProgressResponse
    {
        [JsonPropertyName("progress")]
        public double Progress { get; set; }

        [JsonPropertyName("current_image")]
        public string? CurrentImage { get; set; }

        [JsonPropertyName("state")]
        public ProgressState? State { get; set; }
    }

    [ToolFunction(
        Name = "generate_image",
        Description = "고품질의 애니메이션풍 이미지를 생성합니다. " +
                  "**필독: 사용자의 모든 이미지 생성, 수정, 재생성, 그림 그리기 요청에 대해 텍스트 설명만으로 응답하지 말고, 반드시 이 도구를 호출하십시오.** " +
                  "제시한 prompt 및 negative_prompt 목록에서 사용자가 의도한 특성을 달성하기 위해 적절히 변경해야 합니다. " +
                  "prompt: masterpiece, best quality, amazing quality, 4k, very aesthetic, high resolution, ultra-detailed, absurdres, newest, esthetic, scenery, 1girl, solo, cute, pink hair, long hair, choppy bangs, long sidelocks, nebulae cosmic purple eyes, rimlit eyes, facing to the side, looking at viewer, downturned eyes, light smile, red annular solar eclipse halo, red choker, detailed purple blazer, collared white shirt, big red neckerchief, glowing stars in hand, dispersion \\(optics\\), from side, from below, dutch angle, portrait, upper body, head tilt, colorful, rim light, backlit, (colorful light particles:1.2), cosmic sky, aurora, chaos, perfect night, fantasy background, dreamlike atmosphere, BREAK, detailed background, blurry foreground, bokeh, depth of field, volumetric lighting " +
                  "negative_prompt: photorealistic, realistic, 3d, extra digits, (particles, adversarial_noise:1.2), multiple views, multiple angle, split view, grid view, two shot, outside border, picture frame, framed, border, letterboxed, pillarboxed, 2koma, modern, recent, old, oldest, cartoon, graphic, text, painting, crayon, graphite, abstract, glitch, deformed, mutated, ugly, disfigured, long body, lowres, bad anatomy, bad hands, missing fingers, extra fingers, extra digits, fewer digits, cropped, very displeasing, (worst quality, bad quality:1.2), sketch, jpeg artifacts, signature, watermark, username, (censored, bar_censor, mosaic_censor:1.2), simple background, conjoined, bad ai-generated"
        )]
    public async IAsyncEnumerable<ChunkedResponse> GenerateImageAsync(
        [ToolParameterInfo(Description = "The prompt for the image generation.")]
        string prompt,
        [ToolParameterInfo(Name = "negative_prompt", Description = "The negative prompt for the image generation.")]
        string negativePrompt,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (logger.IsEnabled(LogLevel.Trace))
        {
            logger.LogTrace("prompt: {prompt}", prompt);
            logger.LogTrace("negative_prompt: {negativePrompt}", negativePrompt);
        }

        var payload = new Txt2ImgRequest
        {
            Prompt = prompt,
            NegativePrompt = negativePrompt,
            CfgScale = 4.5,
            SamplerName = "Euler a",
            EnableHires = true,
            HiresUpscaler = "R-ESRGAN 4x+ Anime6B",
            DesnoisingStrength = 0.7
        };

        var generateTask = http.PostAsJsonAsync("/sdapi/v1/txt2img", payload, cancellationToken);
        string fileName = $"{Guid.NewGuid()}.png";
        string? imageCreated = null;

        int skipIterations = 0;
        int imageIndex = 0;
        while (!generateTask.IsCompleted)
        {
            ProgressResponse? progressResponse;
            ChunkedResponse? yield = null;
            try
            {
                bool skipCurrentImage = --skipIterations >= 0;
                var resposne = await http.GetAsync($"/sdapi/v1/progress?skip_current_image={skipCurrentImage.ToString().ToLower()}", cancellationToken);
                resposne.EnsureSuccessStatusCode();
                progressResponse = await resposne.Content.ReadFromJsonAsync<ProgressResponse>(cancellationToken);
                if (progressResponse != null)
                {
                    int step = progressResponse.State?.SamplingStep ?? 0;
                    int totalSteps = progressResponse.State?.SamplingSteps ?? 0;

                    if (progressResponse.CurrentImage != null)
                    {
                        string filePath;
                        if (imageCreated != null)
                        {
                            filePath = Path.Combine("StableDiffusion", "GeneratedImages", imageCreated);
                            if (File.Exists(filePath))
                            {
                                File.Delete(filePath);
                            }
                        }

                        imageCreated = $"{imageIndex++}_{fileName}";
                        filePath = Path.Combine("StableDiffusion", "GeneratedImages", imageCreated);
                        await SaveAsFileAsync(filePath, progressResponse.CurrentImage);
                        skipIterations = 5;
                    }

                    string? previewUri = imageCreated != null
                        ? $"/stable-diffusion/generated/{imageCreated}"
                        : null;

                    yield = new ChunkedResponse
                    {
                        Type = ChunkedResponse.Types.ToolContent,
                        Content = previewUri != null ? $"![Image]({previewUri}){{width={ImageWidth} height={ImageHeight}}}" : null,
                        Hint = new ImageGenerationHint(progressResponse.Progress * 100, step, totalSteps),
                    };
                }
            }
            catch
            {
            }

            if (yield != null)
            {
                yield return yield;
            }

            await Task.WhenAny(Task.Delay(TimeSpan.FromSeconds(1), cancellationToken), generateTask);
        }

        var response = await generateTask;
        var result = await response.Content.ReadFromJsonAsync<Txt2ImgResponse>(cancellationToken: cancellationToken);
        if (result?.Images == null || result.Images.Length == 0)
        {
            var yield = new ChunkedResponse
            {
                Type = ChunkedResponse.Types.ToolContent,
                Content = "Generation aborted.",
            };
            if (imageCreated != null)
            {
                yield.Content = $"![Image]({imageCreated}){{width={ImageWidth} height={ImageHeight}}}\n\n{yield.Content}";
            }

            yield return yield;
            yield return new ChunkedResponse
            {
                Type = ChunkedResponse.Types.ToolResult,
                Content = JsonSerializer.Serialize(new
                {
                    status = "error",
                    message = "no image generated"
                })
            };

            yield break;
        }

        {
            string filePath;
            if (imageCreated != null)
            {
                filePath = Path.Combine("StableDiffusion", "GeneratedImages", imageCreated);
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            }
            
            filePath = Path.Combine("StableDiffusion", "GeneratedImages", fileName);
            await SaveAsFileAsync(filePath, result.Images[0]);

            string imageUri = $"/stable-diffusion/generated/{fileName}";
            yield return new ChunkedResponse
            {
                Type = ChunkedResponse.Types.ToolContent,
                Content = $"![Image]({imageUri}){{width={ImageWidth} height={ImageHeight}}}",
            };

            yield return new ChunkedResponse
            {
                Type = ChunkedResponse.Types.ToolResult,
                Content = JsonSerializer.Serialize(new
                {
                    status = "success",
                    image_uri = imageUri,
                    width = ImageWidth,
                    height = ImageHeight
                })
            };
        }

        yield break;

        async ValueTask SaveAsFileAsync(string filePath, string base64Image)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            byte[] imageBytes = Convert.FromBase64String(base64Image);
            await File.WriteAllBytesAsync(filePath, imageBytes, cancellationToken);
        }
    }
}
