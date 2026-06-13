using AI;
using Discord.WebSocket;
using DiscordBot.Repositories;

namespace DiscordBot.Services;

internal class DiscordChatAttachmentTools(
    SocketMessage message,
    IChatAttachmentRepository chatAttachmentRepository,
    ILogger<DiscordChatAttachmentTools> logger)
{
    private const int DefaultMaxCharacters = 24_000;
    private const int MaxCharactersLimit = 60_000;

    [ToolFunction(
        Name = "load_chat_attachment",
        Description = @"현재 메시지에 직접 첨부되지 않은 과거 채팅 문서가 필요할 때 호출합니다.

사용자가 '아까 올린 pdf', '위 문서', '방금 업데이트 내역 파일', '그 txt', '답글 단 문서'처럼 과거 문서 attachment를 가리키며 요약, 비교, 질의응답을 요청할 때 사용하세요.

target 값:
- latest: 현재 채널에서 이 메시지보다 전에 올라온 가장 최근 문서 1개를 가져옵니다.
- replied: 사용자가 답글을 단 원본 메시지의 문서를 가져옵니다.
- message_id: message_id 파라미터로 지정한 메시지의 문서를 가져옵니다. Discord 메시지 URL 전체를 message_id에 넣어도 됩니다.

현재 사용자의 메시지에 문서가 직접 첨부되어 있다면 이미 AI 입력에 포함되어 있으므로 이 도구를 호출하지 마세요.")]
    public async Task<ToolExecutionResult> LoadChatAttachmentAsync(
        [ToolParameterInfo(Description = "문서를 찾는 방식입니다. latest, replied, message_id 중 하나를 사용하세요. 기본값은 latest입니다.")]
        string target = "latest",
        [ToolParameterInfo(Description = "target이 message_id일 때 사용할 Discord 메시지 ID 또는 메시지 URL입니다.")]
        string? message_id = null,
        [ToolParameterInfo(Description = "응답에 포함할 추출 텍스트 최대 글자 수입니다. 기본 24000, 최대 60000입니다.")]
        int max_characters = DefaultMaxCharacters,
        CancellationToken cancellationToken = default)
    {
        var normalizedTarget = string.IsNullOrWhiteSpace(target)
            ? "latest"
            : target.Trim().ToLowerInvariant();
        var channelId = message.Channel.Id.ToString();

        IReadOnlyList<ChatAttachmentData> attachments = normalizedTarget switch
        {
            "replied" => await LoadRepliedAttachmentsAsync(channelId, cancellationToken),
            "message_id" => await LoadMessageAttachmentsAsync(channelId, message_id, cancellationToken),
            _ => await LoadLatestAttachmentAsync(channelId, cancellationToken)
        };

        if (attachments.Count == 0)
        {
            return ToolExecutionResult.FromText("조건에 맞는 과거 채팅 문서를 찾지 못했습니다.");
        }

        logger.LogInformation(
            "Loaded {Count} chat attachments from target {Target} for tool call.",
            attachments.Count,
            normalizedTarget);

        max_characters = Math.Clamp(max_characters, 1_000, MaxCharactersLimit);
        return ToolExecutionResult.FromText(DiscordChatAttachmentToolFormatter.BuildAttachmentDetails(attachments, max_characters));
    }

    [ToolFunction(
        Name = "search_chat_attachments",
        Description = @"현재 채팅방에 저장된 문서 attachment의 추출 텍스트를 키워드로 검색합니다.

사용자가 과거에 올린 문서 중 특정 내용, 업데이트 항목, 정책, 오류 코드, 변경 내역을 찾아달라고 할 때 사용하세요.
검색 결과에는 원본 Discord 메시지 링크가 포함됩니다. 사용자 답변에 원본 링크가 필요하면 Link 값을 그대로 복사해 보여주세요.")]
    public async Task<string> SearchChatAttachmentsAsync(
        [ToolParameterInfo(Description = "검색할 키워드들을 콤마(,)로 구분한 문자열입니다. 각 키워드는 2글자 이상이어야 합니다.")]
        string keywords,
        [ToolParameterInfo(Description = "최대 결과 수입니다. 1~20, 기본 5입니다.")]
        int limit = 5,
        [ToolParameterInfo(Description = "조회 시작 날짜/시간입니다. 지정된 timezone 기준입니다. 예: 2026-06-01T00:00:00")]
        string? from_date = null,
        [ToolParameterInfo(Description = "조회 종료 날짜/시간입니다. 지정된 timezone 기준입니다. 예: 2026-06-13T23:59:59")]
        string? to_date = null,
        [ToolParameterInfo(Description = "IANA 타임존 ID입니다. 한국어 사용자의 기본값은 Asia/Seoul입니다.")]
        string timezone = "Asia/Seoul",
        CancellationToken cancellationToken = default)
    {
        var validKeywords = (keywords ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static keyword => keyword.Length >= 2)
            .ToList();
        if (validKeywords.Count == 0)
        {
            return "검색에 사용할 2글자 이상의 키워드가 필요합니다.";
        }

        var tz = DiscordChatAttachmentToolFormatter.ResolveTimeZone(timezone);
        if (!DiscordChatAttachmentToolFormatter.TryParseOptionalDateTime(from_date, tz, out var from, out var fromError))
        {
            return fromError;
        }

        if (!DiscordChatAttachmentToolFormatter.TryParseOptionalDateTime(to_date, tz, out var to, out var toError))
        {
            return toError;
        }

        limit = Math.Clamp(limit, 1, 20);
        var channelId = message.Channel.Id.ToString();
        var results = await chatAttachmentRepository.SearchAsync(
            channelId,
            validKeywords,
            limit,
            from,
            to,
            cancellationToken);

        if (results.Count == 0)
        {
            return "검색 결과가 없습니다.";
        }

        return DiscordChatAttachmentToolFormatter.BuildSearchResults(validKeywords, results, tz);
    }

    private async ValueTask<IReadOnlyList<ChatAttachmentData>> LoadLatestAttachmentAsync(
        string channelId,
        CancellationToken cancellationToken)
    {
        var latest = await chatAttachmentRepository.GetLatestAsync(channelId, message.Timestamp, cancellationToken);
        return latest == null ? [] : [latest];
    }

    private async ValueTask<IReadOnlyList<ChatAttachmentData>> LoadRepliedAttachmentsAsync(
        string channelId,
        CancellationToken cancellationToken)
    {
        var messageId = message.Reference?.MessageId;
        if (messageId == null)
        {
            return [];
        }

        return await chatAttachmentRepository.GetByMessageIdAsync(channelId, messageId.Value.ToString(), cancellationToken);
    }

    private async ValueTask<IReadOnlyList<ChatAttachmentData>> LoadMessageAttachmentsAsync(
        string channelId,
        string? messageIdOrUrl,
        CancellationToken cancellationToken)
    {
        var messageId = DiscordChatAttachmentToolFormatter.ExtractMessageId(messageIdOrUrl);
        if (string.IsNullOrWhiteSpace(messageId))
        {
            return [];
        }

        return await chatAttachmentRepository.GetByMessageIdAsync(channelId, messageId, cancellationToken);
    }
}
