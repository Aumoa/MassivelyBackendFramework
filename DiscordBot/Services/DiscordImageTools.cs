using System.Text.Json;
using AI;
using Discord;
using Discord.WebSocket;
using DiscordBot.Services.ImageGeneration;

namespace DiscordBot.Services;

internal class DiscordImageTools(
    SocketMessage message,
    IImageGenerationClient imageGenerationClient,
    ImagePromptProfileProvider promptProfileProvider,
    ILogger<DiscordImageTools> logger) : IToolFunctionDescriptionProvider
{
    private const string GenerateImageDescription =
        "사용자의 이미지 생성, 그림 생성, 일러스트 생성 요청을 처리합니다. " +
        "사용자가 이미지를 만들어 달라고 하면 텍스트 설명만 하지 말고 반드시 이 도구를 호출하세요. " +
        "이 도구는 positive_prompt와 negative_prompt만 받으며 sampler, scheduler, steps, cfg 같은 workflow 설정은 변경하지 않습니다. " +
        "positive_prompt와 negative_prompt는 영어 태그와 짧은 영어 구문 중심으로 작성하세요. " +
        "사용자 요청에 없는 캐릭터 특징, 머리색, 의상, 배경, 구도는 예시에서 가져오지 마세요. " +
        "사용자가 명시한 주제, 스타일, 구도, 배경, 조명, 분위기를 positive_prompt의 중심에 두고, 원하지 않는 요소와 품질 저하 요소는 negative_prompt에 넣으세요.";

    [ToolFunction(
        Name = "generate_image",
        Description = GenerateImageDescription)]
    public async Task<string> GenerateImageAsync(
        [ToolParameterInfo(Name = "positive_prompt", Description = "사용자 요청을 반영한 최종 긍정 프롬프트입니다. 영어 태그와 짧은 영어 구문 중심으로 작성하세요.")]
        string positivePrompt,
        [ToolParameterInfo(Name = "negative_prompt", Description = "품질 저하, 원하지 않는 요소, 사용자 금지 요소를 담은 최종 부정 프롬프트입니다. 영어 태그와 짧은 영어 구문 중심으로 작성하세요.")]
        string negativePrompt,
        CancellationToken cancellationToken = default)
    {
        IUserMessage? statusMessage = null;
        using var statusCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task? statusUpdateTask = null;
        ImageGenerationProgress? latestProgress = null;
        object latestProgressLock = new();

        try
        {
            statusMessage = await message.Channel.SendMessageAsync("이미지를 생성 중입니다...");
            statusUpdateTask = UpdateStatusMessageAsync(
                statusMessage,
                () =>
                {
                    lock (latestProgressLock)
                    {
                        return latestProgress;
                    }
                },
                statusCts.Token);

            var progress = new Progress<ImageGenerationProgress>(value =>
            {
                lock (latestProgressLock)
                {
                    latestProgress = value;
                }
            });

            var image = await imageGenerationClient.GenerateAsync(
                positivePrompt,
                negativePrompt,
                progress,
                cancellationToken);

            await StopStatusUpdateAsync(statusCts, statusUpdateTask);

            await using var stream = new MemoryStream(image.Bytes, writable: false);
            await message.Channel.SendFileAsync(stream, image.FileName);
            await DeleteStatusMessageAsync(statusMessage);

            return JsonSerializer.Serialize(new
            {
                status = "success",
                message = "이미지를 생성해서 Discord 채널에 업로드했습니다.",
                file_name = image.FileName,
                content_type = image.ContentType
            });
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await StopStatusUpdateAsync(statusCts, statusUpdateTask);
            if (statusMessage != null)
            {
                await statusMessage.ModifyAsync(p => p.Content = "이미지 생성 시간이 초과되었습니다.");
            }

            return JsonSerializer.Serialize(new
            {
                status = "error",
                reason = "timeout",
                message = "이미지 생성 시간이 초과되었습니다."
            });
        }
        catch (Exception e)
        {
            await StopStatusUpdateAsync(statusCts, statusUpdateTask);
            if (statusMessage != null)
            {
                await statusMessage.ModifyAsync(p => p.Content = "이미지 생성에 실패했습니다.");
            }

            logger.LogError(e, "Failed to generate image.");
            return JsonSerializer.Serialize(new
            {
                status = "error",
                reason = "image_generation_failed",
                message = e.Message
            });
        }
    }

    private async Task UpdateStatusMessageAsync(
        IUserMessage statusMessage,
        Func<ImageGenerationProgress?> getProgress,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        string[] frames = ["이미지를 생성 중입니다.", "이미지를 생성 중입니다..", "이미지를 생성 중입니다..."];
        int frameIndex = 0;

        try
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                var elapsed = DateTimeOffset.UtcNow - startedAt;
                var frame = frames[frameIndex++ % frames.Length];
                var progress = getProgress();
                await statusMessage.ModifyAsync(p => p.Content = BuildStatusMessage(frame, elapsed, progress));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Failed to update image generation status message.");
        }
    }

    private static string BuildStatusMessage(
        string frame,
        TimeSpan elapsed,
        ImageGenerationProgress? progress)
    {
        List<string> lines = [frame];
        if (progress != null)
        {
            if (!string.IsNullOrWhiteSpace(progress.NodeTitle))
            {
                lines.Add($"현재 단계: {progress.NodeTitle}");
            }

            if (progress.Value.HasValue && progress.Max is > 0)
            {
                var percent = Math.Clamp(progress.Value.Value / (double)progress.Max.Value * 100, 0, 100);
                lines.Add($"진행률: {progress.Value} / {progress.Max} ({percent:F0}%)");
            }
            else if (!string.IsNullOrWhiteSpace(progress.Status))
            {
                lines.Add($"상태: {progress.Status}");
            }
        }

        lines.Add($"경과 시간: {elapsed:mm\\:ss}");
        return string.Join("\n", lines);
    }

    private static async Task StopStatusUpdateAsync(CancellationTokenSource cts, Task? statusUpdateTask)
    {
        await cts.CancelAsync();
        if (statusUpdateTask == null)
        {
            return;
        }

        try
        {
            await statusUpdateTask;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task DeleteStatusMessageAsync(IUserMessage? statusMessage)
    {
        if (statusMessage == null)
        {
            return;
        }

        try
        {
            await statusMessage.DeleteAsync();
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Failed to delete image generation status message.");
        }
    }

    public string? GetToolFunctionDescription(string functionName)
    {
        return functionName == "generate_image"
            ? promptProfileProvider.BuildToolDescription(GenerateImageDescription)
            : null;
    }
}
