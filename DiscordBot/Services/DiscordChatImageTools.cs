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
