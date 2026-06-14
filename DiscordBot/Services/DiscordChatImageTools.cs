using System.Text.Json;
using System.Text.RegularExpressions;
using AI;
using Discord.WebSocket;
using DiscordBot.Repositories;

namespace DiscordBot.Services;

internal partial class DiscordChatImageTools(
    SocketMessage message,
    IChatImageRepository chatImageRepository,
    ILogger<DiscordChatImageTools> logger)
{
    private const int MaxBatchImageCount = 4;

    [ToolFunction(
        Name = "load_chat_image",
        Description = @"현재 메시지에 직접 첨부되지 않은 과거 채팅 이미지가 필요할 때 호출합니다.

사용자가 '저 이미지', '위 사진', '아까 올린 그림', '방금 이미지', '그 피자 사진'처럼 과거 이미지를 가리키며 질문할 때 사용하세요.
이 도구는 비용 관리를 위해 현재 채널의 이미지 중 정확히 1장만 AI 입력에 로드합니다.

target 값:
- latest: 현재 채널에서 이 메시지보다 전에 올라온 가장 최근 이미지 1장을 가져옵니다.
- replied: 사용자가 답글을 단 원본 메시지의 이미지 1장을 가져옵니다.
- message_id: message_id 파라미터로 지정한 메시지의 이미지 1장을 가져옵니다. Discord 메시지 URL 전체를 message_id에 넣어도 됩니다.

현재 사용자의 메시지에 이미지가 직접 첨부되어 있다면 이미 AI 입력에 포함되어 있으므로 이 도구를 호출하지 마세요.
여러 이미지를 비교하거나 한 번에 여러 장을 읽는 용도로 사용하지 마세요.")]
    public async Task<ToolExecutionResult> LoadChatImageAsync(
        [ToolParameterInfo(Description = "이미지를 찾는 방식입니다. latest, replied, message_id 중 하나를 사용하세요. 기본값은 latest입니다.")]
        string target = "latest",
        [ToolParameterInfo(Description = "target이 message_id일 때 사용할 Discord 메시지 ID 또는 메시지 URL입니다.")]
        string? message_id = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedTarget = string.IsNullOrWhiteSpace(target)
            ? "latest"
            : target.Trim().ToLowerInvariant();

        var channelId = message.Channel.Id.ToString();
        ChatImageData? image = normalizedTarget switch
        {
            "replied" => await LoadRepliedImageAsync(channelId, cancellationToken),
            "message_id" => await LoadMessageImageAsync(channelId, message_id, cancellationToken),
            _ => await chatImageRepository.GetLatestAsync(channelId, message.Timestamp, cancellationToken)
        };

        if (image == null)
        {
            return ToolExecutionResult.FromText("조건에 맞는 과거 채팅 이미지를 찾지 못했습니다.");
        }

        logger.LogInformation(
            "Loaded chat image {ImageId} from message {MessageId} for tool call.",
            image.Id,
            image.MessageId);

        var metadata = new
        {
            status = "loaded",
            message_id = image.MessageId,
            file_name = image.FileName,
            content_type = image.ContentType,
            created_at = image.CreatedAt
        };

        return new ToolExecutionResult
        {
            Content = "과거 채팅에서 이미지 1장을 불러왔습니다.\n" + JsonSerializer.Serialize(metadata),
            Images =
            [
                new ChatImage
                {
                    Base64 = Convert.ToBase64String(image.Data),
                    MediaType = image.ContentType
                }
            ]
        };
    }

    [ToolFunction(
        Name = "load_chat_images",
        Description = @"현재 메시지에 직접 첨부되지 않은 과거 채팅 이미지 여러 장이 필요할 때 호출합니다.

사용자가 '위 이미지 두 장 비교해줘', '아까 올린 사진들이 뭐가 달라?', '지난번 이미지랑 이번 이미지 비교해줘'처럼 여러 과거 이미지를 비교하거나 함께 읽어야 할 때 사용하세요.
비용 관리를 위해 한 번에 최대 4장만 로드합니다.

target 값:
- latest: 현재 채널에서 이 메시지보다 전에 올라온 최근 이미지들을 가져옵니다.
- replied: 사용자가 답글을 단 원본 메시지의 이미지 1장을 가져옵니다. 여러 장 비교가 필요하면 message_ids를 함께 지정하세요.
- message_ids: message_ids 파라미터로 지정한 여러 Discord 메시지의 이미지를 가져옵니다. Discord 메시지 URL을 줄바꿈 또는 콤마로 나열해도 됩니다.

현재 사용자의 메시지에 이미지가 직접 첨부되어 있다면 이미 AI 입력에 포함되어 있으므로, 과거 이미지가 추가로 필요할 때만 호출하세요.")]
    public async Task<ToolExecutionResult> LoadChatImagesAsync(
        [ToolParameterInfo(Description = "이미지를 찾는 방식입니다. latest, replied, message_ids 중 하나를 사용하세요. 기본값은 latest입니다.")]
        string target = "latest",
        [ToolParameterInfo(Description = "target이 message_ids일 때 사용할 Discord 메시지 ID 또는 메시지 URL 목록입니다. 콤마/공백/줄바꿈으로 구분할 수 있습니다.")]
        string message_ids = "",
        [ToolParameterInfo(Description = "가져올 최대 이미지 수입니다. 1~4, 기본 2입니다.")]
        int limit = 2,
        CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, MaxBatchImageCount);
        var normalizedTarget = string.IsNullOrWhiteSpace(target)
            ? "latest"
            : target.Trim().ToLowerInvariant();

        var channelId = message.Channel.Id.ToString();
        IReadOnlyList<ChatImageData> images = normalizedTarget switch
        {
            "replied" => await LoadRepliedImagesAsync(channelId, cancellationToken),
            "message_id" => await LoadMessageImagesAsync(channelId, message_ids, limit, cancellationToken),
            "message_ids" => await LoadMessageImagesAsync(channelId, message_ids, limit, cancellationToken),
            _ => await chatImageRepository.GetLatestAsync(channelId, message.Timestamp, limit, cancellationToken)
        };

        images = images.Take(limit).ToList();
        if (images.Count == 0)
        {
            return ToolExecutionResult.FromText("조건에 맞는 과거 채팅 이미지를 찾지 못했습니다.");
        }

        logger.LogInformation(
            "Loaded {Count} chat images from target {Target} for tool call.",
            images.Count,
            normalizedTarget);

        return new ToolExecutionResult
        {
            Content = BuildImageBatchMetadata(images, limit),
            Images = images
                .Select(static image => new ChatImage
                {
                    Base64 = Convert.ToBase64String(image.Data),
                    MediaType = image.ContentType
                })
                .ToList()
        };
    }

    private async ValueTask<ChatImageData?> LoadRepliedImageAsync(
        string channelId,
        CancellationToken cancellationToken)
    {
        var messageId = message.Reference?.MessageId;
        if (messageId == null)
        {
            return null;
        }

        return await chatImageRepository.GetByMessageIdAsync(channelId, messageId.Value.ToString(), cancellationToken);
    }

    private async ValueTask<IReadOnlyList<ChatImageData>> LoadRepliedImagesAsync(
        string channelId,
        CancellationToken cancellationToken)
    {
        var image = await LoadRepliedImageAsync(channelId, cancellationToken);
        return image == null ? [] : [image];
    }

    private async ValueTask<ChatImageData?> LoadMessageImageAsync(
        string channelId,
        string? messageIdOrUrl,
        CancellationToken cancellationToken)
    {
        var messageId = ExtractMessageId(messageIdOrUrl);
        if (string.IsNullOrWhiteSpace(messageId))
        {
            return null;
        }

        return await chatImageRepository.GetByMessageIdAsync(channelId, messageId, cancellationToken);
    }

    private async ValueTask<IReadOnlyList<ChatImageData>> LoadMessageImagesAsync(
        string channelId,
        string messageIdsOrUrls,
        int limit,
        CancellationToken cancellationToken)
    {
        var messageIds = ExtractMessageIds(messageIdsOrUrls);
        if (messageIds.Count == 0)
        {
            return [];
        }

        return await chatImageRepository.GetByMessageIdsAsync(channelId, messageIds, limit, cancellationToken);
    }

    internal static IReadOnlyList<string> ExtractMessageIds(string? messageIdsOrUrls)
    {
        if (string.IsNullOrWhiteSpace(messageIdsOrUrls))
        {
            return [];
        }

        List<string> messageIds = [];
        foreach (var token in messageIdsOrUrls
            .Split([',', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var messageId = ExtractMessageId(token);
            if (!string.IsNullOrWhiteSpace(messageId)
                && !messageIds.Contains(messageId, StringComparer.Ordinal))
            {
                messageIds.Add(messageId);
            }
        }

        return messageIds;
    }

    internal static string BuildImageBatchMetadata(IReadOnlyList<ChatImageData> images, int requestedLimit)
    {
        var items = images.Select(static image => new
        {
            image_id = image.Id,
            chat_log_id = image.ChatLogId,
            message_id = image.MessageId,
            file_name = image.FileName,
            content_type = image.ContentType,
            width = image.Width,
            height = image.Height,
            created_at = image.CreatedAt
        });

        var metadata = new
        {
            status = "loaded",
            count = images.Count,
            requested_limit = requestedLimit,
            max_count = MaxBatchImageCount,
            images = items
        };

        return $"과거 채팅에서 이미지 {images.Count}장을 불러왔습니다.\n" + JsonSerializer.Serialize(metadata);
    }

    private static string? ExtractMessageId(string? messageIdOrUrl)
    {
        if (string.IsNullOrWhiteSpace(messageIdOrUrl))
        {
            return null;
        }

        var trimmed = messageIdOrUrl.Trim();
        if (trimmed.All(char.IsDigit))
        {
            return trimmed;
        }

        var match = DiscordMessageUrlRegex().Match(trimmed);
        return match.Success ? match.Groups["messageId"].Value : null;
    }

    [GeneratedRegex(@"discord(?:app)?\.com/channels/[^/\s]+/[^/\s]+/(?<messageId>\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex DiscordMessageUrlRegex();
}
