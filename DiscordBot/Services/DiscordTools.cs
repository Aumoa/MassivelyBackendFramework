using AI;
using Discord;
using System.Globalization;
using Discord.WebSocket;
using DiscordBot.Repositories;

namespace DiscordBot.Services;

internal class DiscordTools(SocketSelfUser selfUser, SocketMessage message, IChatLogRepository chatLogRepository, IAppointmentRepository appointmentRepository)
{
    private const int DefaultAppointmentRetentionDays = 30;
    private const int MaxAppointmentRetentionDays = 365;
    private const int MaxReplyContextReplies = 50;
    private const int MaxDiscussionSummaryMessages = 100;
    private const int MaxDiscussionSearchResults = 50;
    private const int MaxDiscussionContextEachSide = 5;
    private static readonly (DayOfWeek Day, string[] Aliases)[] KoreanDayOfWeekAliases =
    [
        (DayOfWeek.Sunday, ["일요일", "일욜"]),
        (DayOfWeek.Monday, ["월요일", "월욜"]),
        (DayOfWeek.Tuesday, ["화요일", "화욜"]),
        (DayOfWeek.Wednesday, ["수요일", "수욜"]),
        (DayOfWeek.Thursday, ["목요일", "목욜"]),
        (DayOfWeek.Friday, ["금요일", "금욜"]),
        (DayOfWeek.Saturday, ["토요일", "토욜"])
    ];

    [ToolFunction(
        Name = "get_chat_history",
        Description = "현재 채팅방의 채팅 기록을 MySQL에서 조회합니다. offset으로 건너뛸 메시지 수, limit으로 가져올 메시지 수를 지정할 수 있으며, from_date/to_date로 특정 기간을 필터링할 수 있습니다. 사용자의 언어에 맞는 timezone을 지정하세요 (한국어: Asia/Seoul, 영어(미국): America/New_York 등). 기본값은 UTC입니다.")]
    public async Task<string> GetChatHistoryAsync(
        [ToolParameterInfo(Description = "가져올 최근 메시지 수 (최대 100개)")]
        int limit,
        [ToolParameterInfo(Description = "건너뛸 메시지 수 (기본값: 0)")]
        int offset = 0,
        [ToolParameterInfo(Description = "조회 시작 날짜/시간 (지정된 timezone 기준, 예: 2026-04-01T00:00:00). 미지정 시 제한 없음")]
        string? from_date = null,
        [ToolParameterInfo(Description = "조회 종료 날짜/시간 (지정된 timezone 기준, 예: 2026-04-09T23:59:59). 미지정 시 제한 없음")]
        string? to_date = null,
        [ToolParameterInfo(Description = "IANA 타임존 ID (예: Asia/Seoul, America/New_York, Europe/London). 기본값: UTC")]
        string? timezone = null,
        CancellationToken cancellationToken = default)
    {
        if (limit > 100) limit = 100;
        if (limit < 1) limit = 1;
        if (offset < 0) offset = 0;

        var tz = ResolveTimeZone(timezone);
        var utcOffset = tz.BaseUtcOffset;

        DateTimeOffset? from = !string.IsNullOrEmpty(from_date) ? ParseInTimeZone(from_date, utcOffset) : null;
        DateTimeOffset? to = !string.IsNullOrEmpty(to_date) ? ParseInTimeZone(to_date, utcOffset) : null;

        var channelId = message.Channel.Id.ToString();
        var logs = await chatLogRepository.GetAsync(channelId, limit, offset, from, to, cancellationToken);

        if (logs.Count == 0)
            return "조회된 메시지가 없습니다.";

        var selfId = selfUser.Id.ToString();
        List<string> lines = [];

        foreach (var log in logs)
        {
            var localTime = TimeZoneInfo.ConvertTimeFromUtc(log.CreatedAt, tz);
            if (log.UserId == selfId)
                lines.Add($"[{localTime:yyyy-MM-dd HH:mm:ss}](나의 응답): {log.Content}");
            else
                lines.Add($"[{localTime:yyyy-MM-dd HH:mm:ss}](사용자 {log.UserId}의 메시지): {log.Content}");
        }

        return string.Join("\n", lines);
    }

    [ToolFunction(
        Name = "search_chat_history",
        Description = @"현재 채팅방의 채팅 기록을 키워드로 검색합니다. 사용자 자연어 요청에서 검색에 도움이 될 키워드(동의어, 관련어 포함)를 여러 개 추출하여 콤마로 구분해 전달하세요. 각 키워드는 2글자 이상이어야 하며 OR 검색으로 동작합니다.

약속, 결정, 주제처럼 의미가 주변 메시지에 나뉘어 있을 수 있으면 검색 결과의 ChatLogId로 get_chat_context를 호출해 앞뒤 대화를 확인하세요.

[중요 — 결과 응답 작성 규칙]
각 결과에는 'Link: https://...' 형식의 메시지 링크가 포함됩니다. 이 URL을 사용자에게 보여주는 답변 본문에 **반드시 그대로(전체 URL을) 복사해서 적어야** Discord가 자동으로 원본 메시지를 인용 카드로 표시합니다.

- ❌ 잘못된 예: '위 링크에서 확인할 수 있어요' (URL을 적지 않으면 사용자에게는 아무것도 보이지 않음)
- ❌ 잘못된 예: '[원본 메시지](링크)' 같이 마크다운 링크로 감싸지 마세요 (인용 카드가 표시되지 않음)
- ✅ 올바른 예: '관련 대화: https://discord.com/channels/123/456/789'
- ✅ 올바른 예: 답변 마지막 줄에 https://discord.com/channels/123/456/789 를 그대로 한 줄로 넣기

Link 값이 '(메시지가 오래되어 참조할 수 없어요)'로 표시되어 있다면 URL이 없으므로 그 문구를 그대로 본문에 인용해서 사용자에게 안내하세요.")]
    public async Task<string> SearchChatHistoryAsync(
        [ToolParameterInfo(Description = "검색할 키워드들을 콤마(,)로 구분한 문자열. 동의어와 관련어를 함께 넣어서 누락을 줄이세요. 각 키워드는 2글자 이상. 예: \"피자, 도미노, 배달\"")]
        string keywords,
        [ToolParameterInfo(Description = "최대 결과 수 (1~50, 기본 20)")]
        int limit = 20,
        [ToolParameterInfo(Description = "조회 시작 날짜/시간 (지정된 timezone 기준, 예: 2026-04-01T00:00:00). 미지정 시 제한 없음")]
        string? from_date = null,
        [ToolParameterInfo(Description = "조회 종료 날짜/시간 (지정된 timezone 기준, 예: 2026-04-09T23:59:59). 미지정 시 제한 없음")]
        string? to_date = null,
        [ToolParameterInfo(Description = "IANA 타임존 ID (예: Asia/Seoul, America/New_York). 기본값: UTC")]
        string? timezone = null,
        CancellationToken cancellationToken = default)
    {
        if (limit > 50) limit = 50;
        if (limit < 1) limit = 1;

        var tz = ResolveTimeZone(timezone);
        var utcOffset = tz.BaseUtcOffset;

        DateTimeOffset? from = !string.IsNullOrEmpty(from_date) ? ParseInTimeZone(from_date, utcOffset) : null;
        DateTimeOffset? to = !string.IsNullOrEmpty(to_date) ? ParseInTimeZone(to_date, utcOffset) : null;

        var validKeywords = (keywords ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(k => k.Length >= 2)
            .ToList();
        if (validKeywords.Count == 0)
            return "검색에 사용할 2글자 이상의 키워드가 필요합니다.";

        var channelId = message.Channel.Id.ToString();
        var logs = await chatLogRepository.SearchAsync(channelId, validKeywords, limit, from, to, cancellationToken);

        if (logs.Count == 0)
            return "검색 결과가 없습니다.";

        var selfId = selfUser.Id.ToString();
        List<string> lines = [$"검색 키워드: {string.Join(", ", validKeywords)}", $"결과 {logs.Count}건:", ""];

        int index = 1;
        foreach (var log in logs)
        {
            var localTime = TimeZoneInfo.ConvertTimeFromUtc(log.CreatedAt, tz);
            var author = log.UserId == selfId ? "나의 응답" : $"사용자 {log.UserId}의 메시지";
            var reference = BuildMessageReference(log);
            lines.Add($"--- 결과 #{index} ---");
            lines.Add($"ChatLogId: {log.Id}");
            lines.Add($"Time: {localTime:yyyy-MM-dd HH:mm:ss}");
            lines.Add($"Author: {author}");
            lines.Add($"Link: {reference}");
            lines.Add($"Content: {log.Content}");
            lines.Add("");
            index++;
        }

        return string.Join("\n", lines);
    }

    [ToolFunction(
        Name = "summarize_recent_discussion",
        Description = """
현재 채팅방의 최근 대화나 특정 주제 대화를 요약하기 위한 발췌를 준비합니다.
사용자가 '어제 얘기 요약해줘', '방탈출 얘기 결론 뭐였어?', '오늘 나온 액션아이템만 정리해줘', '최근 배포 관련 대화 정리해줘'처럼 요청할 때 사용하세요.

topic이 비어 있으면 날짜 범위와 limit 기준으로 최근 대화를 가져옵니다.
topic이 있으면 현재 채팅방에서 주제 키워드를 검색하고, 검색된 메시지 주변 맥락을 함께 가져옵니다.
이 도구는 최종 요약문을 저장하지 않으며 현재 채널에서 저장된 대화만 조회합니다.
""")]
    public async Task<string> SummarizeRecentDiscussionAsync(
        [ToolParameterInfo(Description = "요약할 주제입니다. 비워두면 최근/기간 대화를 요약합니다. 예: 방탈출, 배포, 디스코드 봇")]
        string topic = "",
        [ToolParameterInfo(Description = "요약 모드입니다. summary, decisions, action_items, timeline, open_questions 중 하나입니다. 기본 summary.")]
        string mode = "summary",
        [ToolParameterInfo(Description = "요약에 사용할 최대 메시지 수입니다. 1~100, 기본 80.")]
        int limit = 80,
        [ToolParameterInfo(Description = "조회 시작 날짜/시간 (timezone 기준, 예: 2026-06-13T00:00:00). 미지정 시 제한 없음.")]
        string? from_date = null,
        [ToolParameterInfo(Description = "조회 종료 날짜/시간 (timezone 기준, 예: 2026-06-14T00:00:00). 미지정 시 제한 없음.")]
        string? to_date = null,
        [ToolParameterInfo(Description = "IANA 타임존 ID입니다. 한국어 사용자의 기본값은 Asia/Seoul입니다.")]
        string timezone = "Asia/Seoul",
        [ToolParameterInfo(Description = "원본 Discord 메시지 링크를 발췌에 포함할지 여부입니다. 기본 true.")]
        bool include_links = true,
        [ToolParameterInfo(Description = "topic 검색 결과마다 앞뒤로 포함할 메시지 수입니다. 0~5, 기본 2.")]
        int context_each_side = 2,
        CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, MaxDiscussionSummaryMessages);
        context_each_side = Math.Clamp(context_each_side, 0, MaxDiscussionContextEachSide);

        var tz = ResolveTimeZone(string.IsNullOrWhiteSpace(timezone) ? "Asia/Seoul" : timezone);
        var utcOffset = tz.BaseUtcOffset;
        DateTimeOffset? from = !string.IsNullOrWhiteSpace(from_date) ? ParseInTimeZone(from_date, utcOffset) : null;
        DateTimeOffset? to = !string.IsNullOrWhiteSpace(to_date) ? ParseInTimeZone(to_date, utcOffset) : null;

        var channelId = message.Channel.Id.ToString();
        var selfId = selfUser.Id.ToString();
        var normalizedMode = NormalizeDiscussionMode(mode);
        var keywords = NormalizeDiscussionKeywords(topic);
        var source = keywords.Count == 0
            ? "recent"
            : "topic_search";

        IReadOnlyList<ChatLogData> logs;
        if (keywords.Count == 0)
        {
            logs = await chatLogRepository.GetAsync(channelId, limit, 0, from, to, cancellationToken);
        }
        else
        {
            var searchLimit = Math.Min(limit, MaxDiscussionSearchResults);
            var searchResults = await chatLogRepository.SearchAsync(channelId, keywords, searchLimit, from, to, cancellationToken);
            logs = context_each_side == 0
                ? OrderDiscussionLogs(searchResults).Take(limit).ToList()
                : await LoadDiscussionSearchContextsAsync(
                    channelId,
                    searchResults,
                    context_each_side,
                    limit,
                    cancellationToken);
        }

        if (logs.Count == 0)
        {
            return "요약할 대화를 현재 채팅방의 저장된 채팅 기록에서 찾지 못했습니다.";
        }

        return BuildDiscussionSummaryInput(
            channelId,
            topic,
            normalizedMode,
            limit,
            from,
            to,
            tz,
            keywords,
            logs,
            include_links,
            source,
            context_each_side,
            selfId);
    }

    [ToolFunction(
        Name = "get_chat_by_message_id",
        Description = """
현재 채팅방에서 Discord 메시지 ID 또는 메시지 URL로 저장된 채팅 로그 하나를 직접 조회합니다.
사용자가 Discord 메시지 링크를 붙여넣거나, 특정 메시지 ID를 말하며 그 내용을 확인/분석/참조해 달라고 할 때 사용하세요.
이 도구는 현재 채팅방의 메시지만 조회합니다. 다른 채널 URL은 거부됩니다.
""")]
    public async Task<string> GetChatByMessageIdAsync(
        [ToolParameterInfo(Description = "조회할 Discord 메시지 ID 또는 메시지 URL입니다.")]
        string message_id_or_url,
        [ToolParameterInfo(Description = "IANA 타임존 ID (예: Asia/Seoul, America/New_York). 기본값은 UTC입니다.")]
        string? timezone = null,
        CancellationToken cancellationToken = default)
    {
        var target = ResolveMessageReferenceTarget(message_id_or_url);
        if (target == null)
        {
            return "조회할 Discord 메시지 ID 또는 메시지 URL이 필요합니다.";
        }

        var currentChannelId = message.Channel.Id.ToString();
        if (!IsCurrentChannelTarget(target, currentChannelId))
        {
            return "현재 채팅방의 메시지만 조회할 수 있습니다.";
        }

        var log = await chatLogRepository.GetByMessageIdAsync(currentChannelId, target.MessageId, cancellationToken);
        if (log == null)
        {
            return "해당 메시지를 현재 채팅방의 저장된 채팅 기록에서 찾지 못했습니다.";
        }

        var tz = ResolveTimeZone(timezone);
        var details = BuildChatLogDetails(log, tz, selfUser.Id.ToString(), "메시지 조회 결과:");
        return details + "\n\n응답 규칙: 사용자의 질문에 필요한 내용만 간결하게 답하고, 원본 URL이 필요하면 Link 값을 그대로 적으세요.";
    }

    [ToolFunction(
        Name = "get_reply_thread_context",
        Description = """
현재 채팅방에서 특정 메시지의 답장 흐름을 조회합니다. 메시지가 답장한 부모 메시지와, 해당 메시지에 직접 달린 답장들을 함께 보여줍니다.
사용자가 Discord 답장 흐름, 의견에 대한 후속 반응, 특정 메시지의 전후 논의 맥락을 묻거나 메시지 URL을 주며 '이 흐름을 봐줘'라고 할 때 사용하세요.
message_id_or_url에는 Discord 메시지 ID, 메시지 URL, 또는 사용자가 현재 답장으로 참조한 메시지를 뜻하는 replied를 넣을 수 있습니다.
이 도구는 현재 채팅방의 메시지만 조회합니다.
""")]
    public async Task<string> GetReplyThreadContextAsync(
        [ToolParameterInfo(Description = "조회할 Discord 메시지 ID, 메시지 URL, 또는 현재 사용자 메시지가 답장으로 참조한 메시지를 뜻하는 replied입니다.")]
        string message_id_or_url = "replied",
        [ToolParameterInfo(Description = "가져올 직접 답장 수입니다. 0~50, 기본 20입니다.")]
        int reply_limit = 20,
        [ToolParameterInfo(Description = "IANA 타임존 ID (예: Asia/Seoul, America/New_York). 기본값은 UTC입니다.")]
        string? timezone = null,
        CancellationToken cancellationToken = default)
    {
        var target = ResolveMessageReferenceTarget(message_id_or_url);
        if (target == null)
        {
            return "조회할 Discord 메시지 ID, 메시지 URL, 또는 현재 답장 참조가 필요합니다.";
        }

        var currentChannelId = message.Channel.Id.ToString();
        if (!IsCurrentChannelTarget(target, currentChannelId))
        {
            return "현재 채팅방의 메시지만 조회할 수 있습니다.";
        }

        var targetLog = await chatLogRepository.GetByMessageIdAsync(currentChannelId, target.MessageId, cancellationToken);
        if (targetLog == null)
        {
            return "해당 메시지를 현재 채팅방의 저장된 채팅 기록에서 찾지 못했습니다.";
        }

        ChatLogData? parentLog = null;
        if (!string.IsNullOrWhiteSpace(targetLog.ReferencedMessageId)
            && (string.IsNullOrWhiteSpace(targetLog.ReferencedChannelId)
                || string.Equals(targetLog.ReferencedChannelId, currentChannelId, StringComparison.Ordinal)))
        {
            parentLog = await chatLogRepository.GetByMessageIdAsync(
                currentChannelId,
                targetLog.ReferencedMessageId,
                cancellationToken);
        }

        var replyLimit = Math.Clamp(reply_limit, 0, MaxReplyContextReplies);
        var replies = targetLog.MessageId == null || replyLimit == 0
            ? []
            : await chatLogRepository.GetRepliesAsync(
                currentChannelId,
                targetLog.MessageId,
                replyLimit,
                cancellationToken);

        var tz = ResolveTimeZone(timezone);
        return BuildReplyThreadContext(targetLog, parentLog, replies, tz, selfUser.Id.ToString());
    }

    [ToolFunction(
        Name = "get_chat_context",
        Description = """
현재 채팅방에서 특정 채팅 로그 주변 대화를 조회합니다. search_chat_history 결과의 ChatLogId를 chat_log_id로 넣으면, 그 메시지 앞뒤 대화를 함께 볼 수 있습니다.

사용자가 '방탈'처럼 짧은 키워드만 언급했거나, 약속/결정/주제가 여러 메시지에 나뉘어 있을 때는 먼저 search_chat_history로 핵심 메시지를 찾고 이 도구로 주변 대화를 확인하세요.
이 도구는 현재 채팅방의 메시지만 조회하며, 다른 채널의 대화는 볼 수 없습니다.
""")]
    public async Task<string> GetChatContextAsync(
        [ToolParameterInfo(Description = "search_chat_history 결과의 ChatLogId.")]
        int chat_log_id,
        [ToolParameterInfo(Description = "대상 메시지 이전으로 가져올 메시지 수. 0~20, 기본 5.")]
        int before = 5,
        [ToolParameterInfo(Description = "대상 메시지 이후로 가져올 메시지 수. 0~20, 기본 5.")]
        int after = 5,
        [ToolParameterInfo(Description = "IANA 타임존 ID (예: Asia/Seoul, America/New_York). 기본값: UTC")]
        string? timezone = null,
        CancellationToken cancellationToken = default)
    {
        if (chat_log_id <= 0)
        {
            return "조회할 ChatLogId가 필요합니다.";
        }

        before = Math.Clamp(before, 0, 20);
        after = Math.Clamp(after, 0, 20);

        var tz = ResolveTimeZone(timezone);
        var channelId = message.Channel.Id.ToString();
        var logs = await chatLogRepository.GetContextAsync(channelId, chat_log_id, before, after, cancellationToken);
        if (logs.Count == 0)
        {
            return "해당 채팅 로그를 현재 채팅방에서 찾지 못했습니다.";
        }

        var selfId = selfUser.Id.ToString();
        List<string> lines = [$"채팅 로그 {chat_log_id} 주변 대화:"];
        foreach (var log in logs)
        {
            var marker = log.Id == chat_log_id ? " <== 검색된 메시지" : "";
            var localTime = TimeZoneInfo.ConvertTimeFromUtc(log.CreatedAt, tz);
            var author = log.UserId == selfId ? "나의 응답" : $"사용자 {log.UserId}의 메시지";
            var reference = BuildMessageReference(log);
            lines.Add("");
            lines.Add($"ChatLogId: {log.Id}{marker}");
            lines.Add($"Time: {localTime:yyyy-MM-dd HH:mm:ss}");
            lines.Add($"Author: {author}");
            lines.Add($"Link: {reference}");
            lines.Add($"Content: {log.Content}");
        }

        return string.Join("\n", lines);
    }

    private static string BuildMessageReference(ChatLogData log)
    {
        if (string.IsNullOrEmpty(log.MessageId))
            return "(메시지가 오래되어 참조할 수 없어요)";

        return BuildMessageReference(log.GuildId, log.ChannelId, log.MessageId);
    }

    internal static string BuildMessageReference(string? guildId, string channelId, string messageId)
    {
        var guildPart = string.IsNullOrEmpty(guildId) ? "@me" : guildId;
        return $"https://discord.com/channels/{guildPart}/{channelId}/{messageId}";
    }

    internal static DiscordMessageReferenceTarget? ParseMessageReferenceTarget(string? messageIdOrUrl)
    {
        if (string.IsNullOrWhiteSpace(messageIdOrUrl))
        {
            return null;
        }

        var trimmed = messageIdOrUrl.Trim();
        if (trimmed.All(char.IsDigit))
        {
            return new DiscordMessageReferenceTarget(trimmed, null, null);
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            || !IsDiscordHost(uri.Host))
        {
            return null;
        }

        var segments = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Uri.UnescapeDataString)
            .ToArray();

        if (segments.Length < 4
            || !string.Equals(segments[0], "channels", StringComparison.OrdinalIgnoreCase)
            || (!string.Equals(segments[1], "@me", StringComparison.OrdinalIgnoreCase) && !segments[1].All(char.IsDigit))
            || !segments[2].All(char.IsDigit)
            || !segments[3].All(char.IsDigit))
        {
            return null;
        }

        var guildId = string.Equals(segments[1], "@me", StringComparison.OrdinalIgnoreCase)
            ? null
            : segments[1];
        return new DiscordMessageReferenceTarget(segments[3], segments[2], guildId);
    }

    internal static string BuildChatLogDetails(
        ChatLogData log,
        TimeZoneInfo timezone,
        string selfUserId,
        string? heading = null)
    {
        var localTime = TimeZoneInfo.ConvertTimeFromUtc(log.CreatedAt, timezone);
        List<string> lines = [];
        if (!string.IsNullOrWhiteSpace(heading))
        {
            lines.Add(heading);
        }

        lines.Add($"ChatLogId: {log.Id}");
        lines.Add($"Time: {localTime:yyyy-MM-dd HH:mm:ss}");
        lines.Add($"Author: {FormatAuthor(log, selfUserId)}");
        lines.Add($"Link: {BuildMessageReference(log)}");
        if (!string.IsNullOrWhiteSpace(log.ReferencedMessageId))
        {
            var referencedChannelId = string.IsNullOrWhiteSpace(log.ReferencedChannelId)
                ? log.ChannelId
                : log.ReferencedChannelId;
            lines.Add($"ReplyTo: {BuildMessageReference(log.ReferencedGuildId, referencedChannelId, log.ReferencedMessageId)}");
        }

        lines.Add($"Content: {log.Content}");
        return string.Join("\n", lines);
    }

    internal static string BuildReplyThreadContext(
        ChatLogData target,
        ChatLogData? parent,
        IReadOnlyList<ChatLogData> replies,
        TimeZoneInfo timezone,
        string selfUserId)
    {
        List<string> lines = ["답장 흐름 조회 결과:"];

        lines.Add("");
        lines.Add(parent == null
            ? "상위 참조 메시지: 저장된 현재 채팅방 로그에서 찾지 못했습니다."
            : BuildChatLogDetails(parent, timezone, selfUserId, "상위 참조 메시지:"));

        lines.Add("");
        lines.Add(BuildChatLogDetails(target, timezone, selfUserId, "대상 메시지:"));

        lines.Add("");
        lines.Add($"직접 답장 {replies.Count}건:");
        foreach (var reply in replies)
        {
            lines.Add("");
            lines.Add(BuildChatLogDetails(reply, timezone, selfUserId));
        }

        lines.Add("");
        lines.Add("응답 규칙: 답장 흐름을 바탕으로 사용자의 질문에 필요한 맥락만 간결하게 정리하세요. 원본 URL이 필요하면 Link 값을 그대로 적으세요.");
        return string.Join("\n", lines);
    }

    internal static IReadOnlyList<string> NormalizeDiscussionKeywords(string? topic)
    {
        var normalized = (topic ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return [];
        }

        List<string> keywords = [];
        if (normalized.Length >= 2)
        {
            keywords.Add(normalized);
        }

        foreach (var keyword in normalized.Split(
                     [',', ';', '\r', '\n', '\t', ' '],
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (keyword.Length >= 2)
            {
                keywords.Add(keyword);
            }
        }

        return keywords
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal static string NormalizeDiscussionMode(string? mode)
    {
        return (mode ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "decision" or "decisions" => "decisions",
            "action" or "actions" or "action_item" or "action_items" => "action_items",
            "timeline" => "timeline",
            "question" or "questions" or "open_question" or "open_questions" => "open_questions",
            _ => "summary"
        };
    }

    internal static string BuildDiscussionSummaryInput(
        string channelId,
        string? topic,
        string mode,
        int limit,
        DateTimeOffset? from,
        DateTimeOffset? to,
        TimeZoneInfo timezone,
        IReadOnlyList<string> keywords,
        IReadOnlyList<ChatLogData> logs,
        bool includeLinks,
        string source,
        int contextEachSide,
        string selfUserId)
    {
        List<string> lines =
        [
            "요약 대상:",
            $"- 채널: 현재 채널 ({channelId})",
            $"- 조회 방식: {FormatDiscussionSource(source)}",
            $"- 요약 모드: {FormatDiscussionMode(mode)}",
            $"- 주제: {NormalizeOptionalDisplay(topic)}",
            $"- 키워드: {(keywords.Count == 0 ? "없음" : string.Join(", ", keywords))}",
            $"- 범위: {FormatDiscussionRange(from, to, timezone)}",
            $"- 메시지 수: {logs.Count}개 (최대 {limit}개)",
            $"- 검색 주변 맥락: {(keywords.Count == 0 ? "해당 없음" : $"앞뒤 {contextEachSide}개")}",
            "",
            "[대화 발췌]"
        ];

        for (int i = 0; i < logs.Count; i++)
        {
            lines.Add("");
            lines.Add($"--- 메시지 #{i + 1} ---");
            lines.Add(BuildDiscussionLogExcerpt(logs[i], timezone, selfUserId, includeLinks));
        }

        lines.Add("");
        lines.Add("[응답 규칙]");
        lines.AddRange(BuildDiscussionModeRules(mode, includeLinks));
        return string.Join("\n", lines);
    }

    private async ValueTask<IReadOnlyList<ChatLogData>> LoadDiscussionSearchContextsAsync(
        string channelId,
        IReadOnlyList<ChatLogData> searchResults,
        int contextEachSide,
        int limit,
        CancellationToken cancellationToken)
    {
        Dictionary<long, ChatLogData> logs = [];
        foreach (var result in searchResults)
        {
            var context = await chatLogRepository.GetContextAsync(
                channelId,
                result.Id,
                contextEachSide,
                contextEachSide,
                cancellationToken);
            foreach (var log in context)
            {
                logs.TryAdd(log.Id, log);
            }
        }

        return OrderDiscussionLogs(logs.Values)
            .Take(limit)
            .ToList();
    }

    private static IOrderedEnumerable<ChatLogData> OrderDiscussionLogs(IEnumerable<ChatLogData> logs)
    {
        return logs
            .OrderBy(log => log.CreatedAt)
            .ThenBy(log => log.Id);
    }

    private static string BuildDiscussionLogExcerpt(
        ChatLogData log,
        TimeZoneInfo timezone,
        string selfUserId,
        bool includeLinks)
    {
        var localTime = TimeZoneInfo.ConvertTimeFromUtc(log.CreatedAt, timezone);
        List<string> lines =
        [
            $"ChatLogId: {log.Id}",
            $"Time: {localTime:yyyy-MM-dd HH:mm:ss}",
            $"Author: {FormatAuthor(log, selfUserId)}"
        ];

        if (includeLinks)
        {
            lines.Add($"Link: {BuildMessageReference(log)}");
            if (!string.IsNullOrWhiteSpace(log.ReferencedMessageId))
            {
                var referencedChannelId = string.IsNullOrWhiteSpace(log.ReferencedChannelId)
                    ? log.ChannelId
                    : log.ReferencedChannelId;
                lines.Add($"ReplyTo: {BuildMessageReference(log.ReferencedGuildId, referencedChannelId, log.ReferencedMessageId)}");
            }
        }

        lines.Add($"Content: {log.Content}");
        return string.Join("\n", lines);
    }

    private static IEnumerable<string> BuildDiscussionModeRules(string mode, bool includeLinks)
    {
        yield return "- 제공된 [대화 발췌]만 근거로 삼고, 발췌에 없는 내용은 확정하지 마세요.";
        yield return "- 먼저 3~6줄의 핵심 요약을 작성하세요.";

        switch (mode)
        {
            case "decisions":
                yield return "- 결정된 사항과 아직 결정되지 않은 사항을 분리하세요.";
                yield return "- 결정 근거가 약하면 '명확히 결정되지 않음'으로 표시하세요.";
                break;
            case "action_items":
                yield return "- 액션아이템은 담당자, 할 일, 기한이 발췌에서 확인될 때만 적으세요.";
                yield return "- 담당자나 기한이 불명확하면 '미정'으로 표시하고 임의로 만들지 마세요.";
                break;
            case "timeline":
                yield return "- 중요한 흐름을 시간순으로 정리하세요.";
                yield return "- 중복 메시지는 합쳐서 설명하고, 방향이 바뀐 지점을 표시하세요.";
                break;
            case "open_questions":
                yield return "- 아직 답이 없거나 추가 확인이 필요한 질문만 추려 주세요.";
                yield return "- 이미 결론이 난 질문은 제외하거나 결론과 함께 짧게 표시하세요.";
                break;
            default:
                yield return "- 결정사항, 액션아이템, 미해결 질문이 보이면 짧은 별도 목록으로 덧붙이세요.";
                break;
        }

        yield return includeLinks
            ? "- 근거가 중요한 항목에는 Link URL을 그대로 포함하세요."
            : "- 원본 링크는 생략하고 내용 중심으로 정리하세요.";
    }

    private static string FormatDiscussionSource(string source)
    {
        return source == "topic_search"
            ? "주제 검색 + 주변 맥락"
            : "최근/기간 대화";
    }

    private static string FormatDiscussionMode(string mode)
    {
        return mode switch
        {
            "decisions" => "결정사항",
            "action_items" => "액션아이템",
            "timeline" => "타임라인",
            "open_questions" => "미해결 질문",
            _ => "일반 요약"
        };
    }

    private static string FormatDiscussionRange(DateTimeOffset? from, DateTimeOffset? to, TimeZoneInfo timezone)
    {
        var fromText = from.HasValue
            ? FormatDiscussionDateTime(from.Value, timezone)
            : "제한 없음";
        var toText = to.HasValue
            ? FormatDiscussionDateTime(to.Value, timezone)
            : "제한 없음";
        return $"{fromText} ~ {toText} ({timezone.Id})";
    }

    private static string FormatDiscussionDateTime(DateTimeOffset value, TimeZoneInfo timezone)
    {
        return TimeZoneInfo.ConvertTime(value, timezone).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }

    private static string NormalizeOptionalDisplay(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "없음" : value.Trim();
    }

    private DiscordMessageReferenceTarget? ResolveMessageReferenceTarget(string? messageIdOrUrl)
    {
        if (string.IsNullOrWhiteSpace(messageIdOrUrl)
            || string.Equals(messageIdOrUrl.Trim(), "replied", StringComparison.OrdinalIgnoreCase))
        {
            return ResolveCurrentReplyTarget();
        }

        return ParseMessageReferenceTarget(messageIdOrUrl);
    }

    private DiscordMessageReferenceTarget? ResolveCurrentReplyTarget()
    {
        var messageId = message.Reference?.MessageId.IsSpecified == true
            ? message.Reference.MessageId.Value.ToString()
            : null;
        if (string.IsNullOrWhiteSpace(messageId))
        {
            return null;
        }

        var channelId = message.Reference?.ChannelId.ToString();
        var guildId = message.Reference?.GuildId.IsSpecified == true
            ? message.Reference.GuildId.Value.ToString()
            : null;
        return new DiscordMessageReferenceTarget(messageId, channelId, guildId);
    }

    private static bool IsCurrentChannelTarget(DiscordMessageReferenceTarget target, string currentChannelId)
    {
        return string.IsNullOrWhiteSpace(target.ChannelId)
            || string.Equals(target.ChannelId, currentChannelId, StringComparison.Ordinal);
    }

    private static string FormatAuthor(ChatLogData log, string selfUserId)
    {
        return log.UserId == selfUserId
            ? "나의 응답"
            : $"사용자 {log.UserId}의 메시지";
    }

    private static bool IsDiscordHost(string host)
    {
        return string.Equals(host, "discord.com", StringComparison.OrdinalIgnoreCase)
            || string.Equals(host, "www.discord.com", StringComparison.OrdinalIgnoreCase)
            || string.Equals(host, "discordapp.com", StringComparison.OrdinalIgnoreCase)
            || string.Equals(host, "www.discordapp.com", StringComparison.OrdinalIgnoreCase);
    }

    internal sealed record DiscordMessageReferenceTarget(string MessageId, string? ChannelId, string? GuildId);

    [ToolFunction(
        Name = "remember_appointment",
        Description = """
현재 채널의 공유 약속 또는 일정을 기억해야 할 때 사용합니다. 사용자가 '약속 기억해줘', '내일 3시 회의 저장해줘', '다음 주 금요일 저녁 약속 잊지 마'처럼 말하면 호출하세요.

[중요]
- starts_at에는 반드시 사용자의 자연어 날짜를 해석한 정식 날짜/시간을 ISO 형식으로 넣으세요. 예: 2026-05-28T15:00:00
- '내일', '다음 주 금요일', '저녁'처럼 상대적이거나 애매한 표현은 현재 날짜를 기준으로 해석하세요. 현재 날짜를 모르면 먼저 get_current_date를 호출하세요.
- original_date_text에는 날짜 해석에 사용한 사용자의 원문 표현을 넣으세요. 예: '이번주 일요일', '내일', '5월 31일'. 요일이 포함되어 있으면 starts_at의 실제 요일과 반드시 일치해야 합니다.
- 현재 메시지에 약속 제목/날짜가 충분하지 않으면 먼저 search_chat_history와 get_chat_context로 현재 채팅방의 주변 대화를 확인하세요. 예: 사용자가 '방탈 약속 기억해줘'라고만 말하면 '방탈'을 검색하고 주변 대화에서 날짜를 찾으세요.
- 날짜가 불분명하면 이 도구를 호출하지 말고 사용자에게 정확한 날짜를 물어보세요.
- 시간이 정해지지 않은 약속은 저장할 수 있습니다. 이 경우 starts_at에는 timezone offset이나 시간을 붙이지 않은 날짜만 넣고 has_time=false로 호출하세요. 임의로 오후 2시 같은 시간을 만들지 마세요.
- search_chat_history/get_chat_context로 약속 근거가 된 과거 메시지를 찾았다면 source_chat_log_id에 해당 ChatLogId를 넣으세요. 그러면 약속 조회 시 원본 약속 대화로 링크됩니다.
- 약속 저장은 현재 채널 공유용입니다. 같은 채널의 사용자는 이 약속을 조회, 수정, 삭제할 수 있습니다. 다른 채널에서는 보이지 않습니다.
- user_id는 만든 사람 기록으로만 저장됩니다. guild_id, channel_id, user_id는 프로그램이 현재 메시지에서 자동으로 고정합니다.
- 저장된 약속에는 원본 Discord 메시지 링크가 함께 보존됩니다. 조회 응답에는 해당 링크를 그대로 보여주세요.
- 지난 약속은 기본적으로 약속 시간 30일 뒤 자동으로 잊어버립니다.
""")]
    public async Task<string> RememberAppointmentAsync(
        [ToolParameterInfo(Description = "약속 제목. 짧고 구체적으로 작성하세요. 예: 치과 예약, 프로젝트 회의")]
        string title,
        [ToolParameterInfo(Description = "약속 시작 날짜/시간. ISO 형식 권장. 예: 2026-05-28T15:00:00 또는 2026-05-28T15:00:00+09:00")]
        string starts_at,
        [ToolParameterInfo(Description = "날짜 해석에 사용한 사용자의 원문 표현. 예: 이번주 일요일, 다음 주 금요일, 5월 31일")]
        string original_date_text,
        [ToolParameterInfo(Description = "약속 시간이 명확히 정해졌으면 true, 날짜만 정해지고 시간이 미정이면 false.")]
        bool has_time = true,
        [ToolParameterInfo(Description = "IANA 타임존 ID. 한국어 사용자의 기본값은 Asia/Seoul입니다.")]
        string timezone = "Asia/Seoul",
        [ToolParameterInfo(Description = "약속에 대한 추가 설명. 없으면 빈 문자열.")]
        string description = "",
        [ToolParameterInfo(Description = "약속 근거가 된 과거 메시지의 ChatLogId. search_chat_history/get_chat_context로 찾은 경우에만 지정하고, 없으면 0.")]
        int source_chat_log_id = 0,
        [ToolParameterInfo(Description = "약속 시간이 지난 뒤 며칠 후 잊어버릴지. 기본 30일, 최대 365일.")]
        int forget_after_days = DefaultAppointmentRetentionDays,
        CancellationToken cancellationToken = default)
    {
        var normalizedTitle = NormalizeAppointmentTitle(title);
        if (string.IsNullOrWhiteSpace(normalizedTitle))
        {
            return "약속 제목이 필요합니다.";
        }

        var tz = ResolveTimeZone(string.IsNullOrWhiteSpace(timezone) ? "Asia/Seoul" : timezone);
        if (!TryParseAppointmentDateTime(starts_at, tz, has_time, out var startsAtUtc, out var effectiveHasTime, out var parseError))
        {
            return parseError;
        }

        var nowUtc = DateTime.UtcNow;
        var localStart = TimeZoneInfo.ConvertTimeFromUtc(startsAtUtc, tz);
        if (!TryValidateOriginalDateText(original_date_text, localStart, tz, nowUtc, out var dateTextError))
        {
            return dateTextError;
        }

        if (!IsFutureOrToday(startsAtUtc, effectiveHasTime, tz, nowUtc))
        {
            return "이미 지난 시간으로 보입니다. 앞으로 있을 약속의 날짜와 시간을 다시 확인해 주세요.";
        }

        forget_after_days = Math.Clamp(forget_after_days, 1, MaxAppointmentRetentionDays);
        var expiresAtUtc = startsAtUtc.AddDays(forget_after_days);
        var guildId = (message.Channel as SocketGuildChannel)?.Guild.Id.ToString();
        var sourceGuildId = guildId;
        var sourceChannelId = message.Channel.Id.ToString();
        var sourceMessageId = message.Id.ToString();
        if (source_chat_log_id > 0)
        {
            var sourceLogs = await chatLogRepository.GetContextAsync(
                message.Channel.Id.ToString(),
                source_chat_log_id,
                0,
                0,
                cancellationToken);
            var sourceLog = sourceLogs.FirstOrDefault(log => log.Id == source_chat_log_id);
            if (sourceLog != null)
            {
                sourceGuildId = sourceLog.GuildId;
                sourceChannelId = sourceLog.ChannelId;
                sourceMessageId = sourceLog.MessageId;
            }
        }

        var input = new AppointmentInput(
            sourceGuildId,
            sourceChannelId,
            message.Author.Id.ToString(),
            sourceMessageId,
            normalizedTitle,
            NormalizeNullable(description, 2048),
            startsAtUtc,
            effectiveHasTime,
            tz.Id,
            expiresAtUtc);

        await appointmentRepository.ExpireOldAsync(nowUtc, cancellationToken);
        var id = await appointmentRepository.AddAsync(input, cancellationToken);
        var startText = FormatAppointmentStart(startsAtUtc, effectiveHasTime, tz);
        var localExpire = TimeZoneInfo.ConvertTimeFromUtc(expiresAtUtc, tz);
        var reference = BuildAppointmentReference(sourceGuildId, sourceChannelId, sourceMessageId);

        return $"""
약속을 저장했습니다.
ID: {id}
제목: {normalizedTitle}
일시: {startText}
원본: {reference}
잊는 시점: {localExpire:yyyy-MM-dd HH:mm:ss} ({tz.Id})
응답 규칙: 사용자에게 약속을 기억했다고 짧게 알려주고, 날짜/시간과 원본 메시지 링크를 함께 확인해 주세요. 원본 URL은 Discord 인용 카드가 뜨도록 그대로 적으세요.
""";
    }

    [ToolFunction(
        Name = "list_appointments",
        Description = """
현재 채널에 저장된 공유 약속을 조회합니다. 사용자가 '약속 알려줘', '이번 주 약속 뭐 있어?', '내일 일정 알려줘'처럼 말하면 호출하세요.

조회 범위는 현재 Discord 채널로 제한됩니다. 다른 채널이나 다른 서버의 약속은 조회할 수 없습니다.
날짜 범위를 지정할 때 from_date/to_date는 timezone 기준으로 해석됩니다.
결과의 원본 URL은 사용자 응답 본문에 그대로 적어야 Discord가 원본 메시지를 인용 카드로 표시합니다.
""")]
    public async Task<string> ListAppointmentsAsync(
        [ToolParameterInfo(Description = "최대 조회 수. 1~50, 기본 10.")]
        int limit = 10,
        [ToolParameterInfo(Description = "조회 시작 날짜/시간. timezone 기준. 예: 2026-05-28T00:00:00. 미지정 시 제한 없음.")]
        string? from_date = null,
        [ToolParameterInfo(Description = "조회 종료 날짜/시간. timezone 기준. 예: 2026-05-28T23:59:59. 미지정 시 제한 없음.")]
        string? to_date = null,
        [ToolParameterInfo(Description = "IANA 타임존 ID. 한국어 사용자의 기본값은 Asia/Seoul입니다.")]
        string timezone = "Asia/Seoul",
        [ToolParameterInfo(Description = "지난 약속도 포함할지 여부. 기본 false.")]
        bool include_past = false,
        CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 50);

        var tz = ResolveTimeZone(string.IsNullOrWhiteSpace(timezone) ? "Asia/Seoul" : timezone);
        if (!TryParseOptionalDateTime(from_date, tz, out var fromUtc, out var fromError))
        {
            return fromError;
        }

        if (!TryParseOptionalDateTime(to_date, tz, out var toUtc, out var toError))
        {
            return toError;
        }

        var nowUtc = DateTime.UtcNow;
        var guildId = (message.Channel as SocketGuildChannel)?.Guild.Id.ToString();
        var channelId = message.Channel.Id.ToString();
        await appointmentRepository.ExpireOldAsync(nowUtc, cancellationToken);
        var appointments = await appointmentRepository.GetActiveAsync(
            channelId,
            guildId,
            nowUtc,
            limit,
            fromUtc,
            toUtc,
            include_past,
            cancellationToken);

        if (appointments.Count == 0)
        {
            return "조회된 약속이 없습니다.";
        }

        List<string> lines = [$"조회된 약속 {appointments.Count}건:"];
        foreach (var appointment in appointments)
        {
            var appointmentTz = ResolveTimeZone(appointment.Timezone);
            var startText = FormatAppointmentStart(EnsureUtc(appointment.StartsAtUtc), appointment.HasTime, appointmentTz);
            var reference = BuildAppointmentReference(appointment);
            lines.Add("");
            lines.Add($"ID: {appointment.Id}");
            lines.Add($"제목: {appointment.Title}");
            lines.Add($"일시: {startText}");
            if (!string.IsNullOrWhiteSpace(appointment.Description))
            {
                lines.Add($"설명: {appointment.Description}");
            }
            lines.Add($"원본: {reference}");
        }

        lines.Add("");
        lines.Add("응답 규칙: 현재 채널의 공유 약속 중 사용자에게 필요한 약속만 간결하게 정리하세요. 원본 URL은 Discord 인용 카드가 뜨도록 그대로 적으세요. 삭제가 필요하면 ID를 기준으로 forget_appointment를 사용할 수 있습니다.");
        return string.Join("\n", lines);
    }

    [ToolFunction(
        Name = "forget_appointment",
        Description = """
사용자가 현재 채널에 저장된 공유 약속을 삭제하거나 잊어달라고 요청할 때 사용합니다.
id는 list_appointments 결과의 ID를 사용하세요. 사용자가 특정 약속을 자연어로만 말해 ID가 불분명하면 먼저 list_appointments로 후보를 조회하거나 사용자에게 확인하세요.
""")]
    public async Task<string> ForgetAppointmentAsync(
        [ToolParameterInfo(Description = "삭제할 약속 ID. list_appointments 결과의 ID.")]
        int id,
        [ToolParameterInfo(Description = "삭제 이유. 없으면 빈 문자열.")]
        string reason = "",
        CancellationToken cancellationToken = default)
    {
        if (id <= 0)
        {
            return "삭제할 약속 ID가 필요합니다. 먼저 약속을 조회해서 ID를 확인해 주세요.";
        }

        var nowUtc = DateTime.UtcNow;
        var guildId = (message.Channel as SocketGuildChannel)?.Guild.Id.ToString();
        var channelId = message.Channel.Id.ToString();
        await appointmentRepository.ExpireOldAsync(nowUtc, cancellationToken);
        var appointment = await appointmentRepository.GetActiveByIdAsync(
            id,
            channelId,
            guildId,
            nowUtc,
            cancellationToken);
        if (appointment == null)
        {
            return "해당 ID의 활성 약속을 찾지 못했습니다.";
        }

        var deleted = await appointmentRepository.DeleteAsync(
            id,
            channelId,
            guildId,
            cancellationToken);
        if (!deleted)
        {
            return "약속 삭제에 실패했습니다. 이미 삭제되었거나 찾을 수 없습니다.";
        }

        var appointmentTz = ResolveTimeZone(appointment.Timezone);
        var startText = FormatAppointmentStart(EnsureUtc(appointment.StartsAtUtc), appointment.HasTime, appointmentTz);
        return $"""
약속을 삭제했습니다.
ID: {appointment.Id}
제목: {appointment.Title}
일시: {startText}
삭제 이유: {(string.IsNullOrWhiteSpace(reason) ? "미지정" : reason)}
응답 규칙: 사용자에게 삭제 완료를 짧게 알려주세요.
""";
    }

    [ToolFunction(
        Name = "update_appointment",
        Description = """
현재 채널에 저장된 공유 약속의 제목, 날짜, 시간, 설명을 수정합니다.
id는 list_appointments 결과의 ID를 사용하세요. ID가 불분명하면 먼저 list_appointments로 후보를 조회하거나 사용자에게 확인하세요.

시간만 나중에 정해진 경우 time에 HH:mm 값을 넣으세요. 예: '방탈출 약속은 오후 3시로 정해졌어' -> 기존 날짜 유지, time='15:00'.
시간이 다시 미정이 된 경우 time_unspecified=true로 호출하세요. 임의의 시간을 만들지 마세요.
date가 비어 있으면 기존 날짜를 유지하고, time이 비어 있으면 기존 시간 상태를 유지합니다.
""")]
    public async Task<string> UpdateAppointmentAsync(
        [ToolParameterInfo(Description = "수정할 약속 ID. list_appointments 결과의 ID.")]
        int id,
        [ToolParameterInfo(Description = "새 제목. 변경하지 않으려면 빈 문자열.")]
        string title = "",
        [ToolParameterInfo(Description = "새 날짜. 예: 2026-05-31. 변경하지 않으려면 빈 문자열.")]
        string date = "",
        [ToolParameterInfo(Description = "새 시간. 24시간 HH:mm 권장. 예: 15:00. 변경하지 않으려면 빈 문자열.")]
        string time = "",
        [ToolParameterInfo(Description = "시간을 미정으로 바꿀지 여부.")]
        bool time_unspecified = false,
        [ToolParameterInfo(Description = "새 설명. 변경하지 않으려면 빈 문자열.")]
        string description = "",
        [ToolParameterInfo(Description = "IANA 타임존 ID. 비워두면 기존 약속의 timezone을 유지합니다.")]
        string timezone = "",
        [ToolParameterInfo(Description = "수정된 약속 시간이 지난 뒤 며칠 후 잊어버릴지. 기본 30일, 최대 365일.")]
        int forget_after_days = DefaultAppointmentRetentionDays,
        CancellationToken cancellationToken = default)
    {
        if (id <= 0)
        {
            return "수정할 약속 ID가 필요합니다. 먼저 약속을 조회해서 ID를 확인해 주세요.";
        }

        var nowUtc = DateTime.UtcNow;
        var guildId = (message.Channel as SocketGuildChannel)?.Guild.Id.ToString();
        var channelId = message.Channel.Id.ToString();
        await appointmentRepository.ExpireOldAsync(nowUtc, cancellationToken);
        var appointment = await appointmentRepository.GetActiveByIdAsync(
            id,
            channelId,
            guildId,
            nowUtc,
            cancellationToken);
        if (appointment == null)
        {
            return "해당 ID의 활성 약속을 찾지 못했습니다.";
        }

        var oldTz = ResolveTimeZone(appointment.Timezone);
        var effectiveTz = ResolveTimeZone(string.IsNullOrWhiteSpace(timezone) ? appointment.Timezone : timezone);
        var oldLocalStart = TimeZoneInfo.ConvertTimeFromUtc(EnsureUtc(appointment.StartsAtUtc), oldTz);

        if (!TryBuildUpdatedLocalDateTime(
                oldLocalStart,
                appointment.HasTime,
                date,
                time,
                time_unspecified,
                effectiveTz,
                out var localStart,
                out var hasTime,
                out var updateError))
        {
            return updateError;
        }

        var startsAtUtc = TimeZoneInfo.ConvertTimeToUtc(localStart, effectiveTz);
        if (!IsFutureOrToday(startsAtUtc, hasTime, effectiveTz, nowUtc))
        {
            return "수정하려는 약속 시간이 이미 지난 시간으로 보입니다. 앞으로 있을 날짜와 시간을 다시 확인해 주세요.";
        }

        var normalizedTitle = string.IsNullOrWhiteSpace(title)
            ? appointment.Title
            : NormalizeAppointmentTitle(title);
        var normalizedDescription = string.IsNullOrWhiteSpace(description)
            ? appointment.Description
            : NormalizeNullable(description, 2048);
        forget_after_days = Math.Clamp(forget_after_days, 1, MaxAppointmentRetentionDays);
        var expiresAtUtc = startsAtUtc.AddDays(forget_after_days);

        var updated = await appointmentRepository.UpdateAsync(
            id,
            channelId,
            guildId,
            normalizedTitle,
            normalizedDescription,
            startsAtUtc,
            hasTime,
            effectiveTz.Id,
            expiresAtUtc,
            cancellationToken);
        if (!updated)
        {
            return "약속 수정에 실패했습니다. 이미 삭제되었거나 찾을 수 없습니다.";
        }

        var startText = FormatAppointmentStart(startsAtUtc, hasTime, effectiveTz);
        var reference = BuildAppointmentReference(appointment);
        return $"""
약속을 수정했습니다.
ID: {appointment.Id}
제목: {normalizedTitle}
일시: {startText}
원본: {reference}
응답 규칙: 사용자에게 수정 완료를 짧게 알려주고, 변경된 날짜/시간을 확인해 주세요.
""";
    }

    [ToolFunction(
        Name = "get_current_date",
        Description = "현재 날짜와 시간을 가져옵니다. 사용자의 언어에 맞는 timezone을 지정하세요 (한국어: Asia/Seoul, 영어(미국): America/New_York 등). 기본값은 UTC입니다.")]
    public Task<string> GetCurrentDateAsync(
        [ToolParameterInfo(Description = "IANA 타임존 ID (예: Asia/Seoul, America/New_York, Europe/London). 기본값: UTC")]
        string? timezone = null,
        CancellationToken cancellationToken = default)
    {
        var tz = ResolveTimeZone(timezone);
        var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        return Task.FromResult($"{now:yyyy-MM-dd HH:mm:ss} ({tz.Id})");
    }

    private static TimeZoneInfo ResolveTimeZone(string? timezone)
    {
        if (string.IsNullOrEmpty(timezone))
            return TimeZoneInfo.Utc;

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timezone);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    private static DateTimeOffset ParseInTimeZone(string dateString, TimeSpan utcOffset)
    {
        if (DateTimeOffset.TryParse(dateString, out var parsed))
        {
            if (parsed.Offset == TimeSpan.Zero && !dateString.EndsWith("Z", StringComparison.OrdinalIgnoreCase))
                return new DateTimeOffset(parsed.DateTime, utcOffset);
            return parsed;
        }

        var dt = DateTime.Parse(dateString);
        return new DateTimeOffset(dt, utcOffset);
    }

    private static string NormalizeAppointmentTitle(string title)
    {
        var normalized = (title ?? string.Empty).Trim();
        return normalized.Length <= 256 ? normalized : normalized[..256];
    }

    private static string? NormalizeNullable(string value, int maxLength)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private static bool TryParseOptionalDateTime(
        string? dateString,
        TimeZoneInfo timezone,
        out DateTime? utcDateTime,
        out string error)
    {
        utcDateTime = null;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(dateString))
        {
            return true;
        }

        if (!TryParseDateTimeInTimeZone(dateString, timezone, out var parsed, out error))
        {
            return false;
        }

        utcDateTime = parsed;
        return true;
    }

    private static bool TryParseDateTimeInTimeZone(
        string dateString,
        TimeZoneInfo timezone,
        out DateTime utcDateTime,
        out string error)
    {
        utcDateTime = default;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(dateString))
        {
            error = "약속 날짜와 시간이 필요합니다. 날짜나 시간이 애매하면 사용자에게 먼저 확인해 주세요.";
            return false;
        }

        if (HasExplicitOffset(dateString))
        {
            if (DateTimeOffset.TryParse(
                    dateString,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces,
                    out var parsedOffset))
            {
                utcDateTime = parsedOffset.UtcDateTime;
                return true;
            }

            error = "약속 날짜/시간을 해석하지 못했습니다. ISO 형식으로 다시 지정해 주세요. 예: 2026-05-28T15:00:00+09:00";
            return false;
        }

        if (!DateTime.TryParse(
                dateString,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var localDateTime)
            && !DateTime.TryParse(
                dateString,
                CultureInfo.CurrentCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out localDateTime))
        {
            error = "약속 날짜/시간을 해석하지 못했습니다. ISO 형식으로 다시 지정해 주세요. 예: 2026-05-28T15:00:00";
            return false;
        }

        localDateTime = DateTime.SpecifyKind(localDateTime, DateTimeKind.Unspecified);
        try
        {
            utcDateTime = TimeZoneInfo.ConvertTimeToUtc(localDateTime, timezone);
            return true;
        }
        catch (ArgumentException)
        {
            error = "해당 시간대에서 사용할 수 없는 날짜/시간입니다. 다른 시간으로 다시 지정해 주세요.";
            return false;
        }
    }

    private static bool TryParseAppointmentDateTime(
        string dateString,
        TimeZoneInfo timezone,
        bool requestedHasTime,
        out DateTime utcDateTime,
        out bool hasTime,
        out string error)
    {
        utcDateTime = default;
        hasTime = false;
        error = string.Empty;
        var explicitTime = HasExplicitTime(dateString);
        if (!requestedHasTime)
        {
            if (explicitTime || HasExplicitOffset(dateString))
            {
                error = "시간이 정해지지 않은 약속은 starts_at에 날짜만 넣어야 합니다. 예: 2026-05-31. 시간이나 timezone offset을 임의로 넣지 말고 has_time=false로 다시 호출하세요.";
                return false;
            }
        }

        if (!TryParseDateTimeInTimeZone(dateString, timezone, out var parsedUtc, out error))
        {
            return false;
        }

        hasTime = requestedHasTime && explicitTime;
        if (requestedHasTime && !explicitTime)
        {
            error = "약속 날짜는 확인했지만 시간이 없습니다. 시간이 미정이면 has_time=false로 저장하고, 시간이 필요하면 사용자에게 확인해 주세요.";
            return false;
        }

        if (!hasTime)
        {
            var local = TimeZoneInfo.ConvertTimeFromUtc(parsedUtc, timezone).Date;
            parsedUtc = TimeZoneInfo.ConvertTimeToUtc(local, timezone);
        }

        utcDateTime = parsedUtc;
        return true;
    }

    private static bool TryValidateOriginalDateText(
        string? originalDateText,
        DateTime localStart,
        TimeZoneInfo timezone,
        DateTime nowUtc,
        out string error)
    {
        error = string.Empty;
        if (!TryGetReferencedDayOfWeek(originalDateText, out var expectedDayOfWeek))
        {
            return true;
        }

        if (localStart.DayOfWeek != expectedDayOfWeek)
        {
            error = $"날짜 표현과 실제 날짜의 요일이 맞지 않습니다. 원문 표현은 '{originalDateText}'이고, 저장하려는 날짜 {localStart:yyyy-MM-dd}은 {GetKoreanDayOfWeek(localStart.DayOfWeek)}입니다. 날짜를 다시 확인한 뒤 호출하세요.";
            return false;
        }

        if (!TryGetRelativeWeekOffset(originalDateText, out var weekOffset))
        {
            return true;
        }

        var today = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, timezone).Date;
        var expectedDate = GetWeekDate(today, expectedDayOfWeek, weekOffset);
        if (localStart.Date == expectedDate)
        {
            return true;
        }

        var relativeWeekText = weekOffset switch
        {
            0 => "이번 주",
            1 => "다음 주",
            2 => "다다음 주",
            _ => $"{weekOffset}주 뒤"
        };
        error = $"날짜 표현과 저장하려는 날짜가 맞지 않습니다. {timezone.Id} 기준 오늘은 {today:yyyy-MM-dd}이고, '{relativeWeekText} {GetKoreanDayOfWeek(expectedDayOfWeek)}'은 {expectedDate:yyyy-MM-dd}입니다. 저장하려는 날짜는 {localStart:yyyy-MM-dd}이므로 다시 확인해 주세요.";
        return false;
    }

    private static bool TryGetReferencedDayOfWeek(string? value, out DayOfWeek dayOfWeek)
    {
        var normalized = NormalizeDateExpression(value);
        foreach (var (day, aliases) in KoreanDayOfWeekAliases)
        {
            if (aliases.Any(normalized.Contains))
            {
                dayOfWeek = day;
                return true;
            }
        }

        dayOfWeek = default;
        return false;
    }

    private static bool TryGetRelativeWeekOffset(string? value, out int weekOffset)
    {
        var normalized = NormalizeDateExpression(value);
        if (normalized.Contains("다다음주"))
        {
            weekOffset = 2;
            return true;
        }

        if (normalized.Contains("다음주") || normalized.Contains("담주"))
        {
            weekOffset = 1;
            return true;
        }

        if (normalized.Contains("이번주"))
        {
            weekOffset = 0;
            return true;
        }

        weekOffset = default;
        return false;
    }

    private static DateTime GetWeekDate(DateTime today, DayOfWeek dayOfWeek, int weekOffset)
    {
        var daysSinceMonday = ((int)today.DayOfWeek + 6) % 7;
        var monday = today.AddDays(-daysSinceMonday).AddDays(weekOffset * 7);
        var targetOffset = dayOfWeek == DayOfWeek.Sunday ? 6 : (int)dayOfWeek - 1;
        return monday.AddDays(targetOffset);
    }

    private static string NormalizeDateExpression(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Replace(" ", string.Empty).Trim().ToLowerInvariant();
    }

    private static bool TryBuildUpdatedLocalDateTime(
        DateTime oldLocalStart,
        bool oldHasTime,
        string date,
        string time,
        bool timeUnspecified,
        TimeZoneInfo timezone,
        out DateTime localStart,
        out bool hasTime,
        out string error)
    {
        error = string.Empty;
        var localDate = oldLocalStart.Date;
        var localTime = oldHasTime ? oldLocalStart.TimeOfDay : TimeSpan.Zero;
        hasTime = oldHasTime;

        if (!string.IsNullOrWhiteSpace(date))
        {
            if (!TryParseLocalDate(date, timezone, out localDate, out error))
            {
                localStart = default;
                return false;
            }
        }

        if (timeUnspecified)
        {
            hasTime = false;
            localTime = TimeSpan.Zero;
        }
        else if (!string.IsNullOrWhiteSpace(time))
        {
            if (!TryParseLocalTime(time, out localTime, out error))
            {
                localStart = default;
                return false;
            }

            hasTime = true;
        }

        localStart = DateTime.SpecifyKind(localDate.Add(localTime), DateTimeKind.Unspecified);
        return true;
    }

    private static bool TryParseLocalDate(string value, TimeZoneInfo timezone, out DateTime date, out string error)
    {
        date = default;
        error = string.Empty;
        if (!TryParseDateTimeInTimeZone(value, timezone, out var parsedUtc, out error))
        {
            error = "날짜를 해석하지 못했습니다. 예: 2026-05-31";
            return false;
        }

        date = TimeZoneInfo.ConvertTimeFromUtc(parsedUtc, timezone).Date;
        return true;
    }

    private static bool TryParseLocalTime(string value, out TimeSpan time, out string error)
    {
        time = default;
        error = string.Empty;
        var normalized = value.Trim();
        if (TimeSpan.TryParse(normalized, CultureInfo.InvariantCulture, out time))
        {
            return true;
        }

        if (DateTime.TryParse(normalized, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed)
            || DateTime.TryParse(normalized, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out parsed))
        {
            time = parsed.TimeOfDay;
            return true;
        }

        error = "시간을 해석하지 못했습니다. 24시간 형식으로 다시 지정해 주세요. 예: 15:00";
        return false;
    }

    private static bool IsFutureOrToday(DateTime startsAtUtc, bool hasTime, TimeZoneInfo timezone, DateTime nowUtc)
    {
        if (hasTime)
        {
            return startsAtUtc >= nowUtc.AddMinutes(-5);
        }

        var startDate = TimeZoneInfo.ConvertTimeFromUtc(startsAtUtc, timezone).Date;
        var today = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, timezone).Date;
        return startDate >= today;
    }

    private static string FormatAppointmentStart(DateTime startsAtUtc, bool hasTime, TimeZoneInfo timezone)
    {
        var localStart = TimeZoneInfo.ConvertTimeFromUtc(EnsureUtc(startsAtUtc), timezone);
        if (hasTime)
        {
            return $"{localStart:yyyy-MM-dd HH:mm:ss} ({timezone.Id})";
        }

        return $"{localStart:yyyy-MM-dd} ({GetKoreanDayOfWeek(localStart.DayOfWeek)}, {timezone.Id}), 시간은 아직 정해지지 않았어요.";
    }

    private static string GetKoreanDayOfWeek(DayOfWeek dayOfWeek)
    {
        return dayOfWeek switch
        {
            DayOfWeek.Sunday => "일요일",
            DayOfWeek.Monday => "월요일",
            DayOfWeek.Tuesday => "화요일",
            DayOfWeek.Wednesday => "수요일",
            DayOfWeek.Thursday => "목요일",
            DayOfWeek.Friday => "금요일",
            DayOfWeek.Saturday => "토요일",
            _ => dayOfWeek.ToString()
        };
    }

    private static bool HasExplicitTime(string value)
    {
        var trimmed = value.Trim();
        var separatorIndex = Math.Max(trimmed.LastIndexOf('T'), trimmed.LastIndexOf(' '));
        if (separatorIndex < 0 || separatorIndex == trimmed.Length - 1)
        {
            return false;
        }

        var timePart = trimmed[(separatorIndex + 1)..];
        return timePart.Contains(':') || timePart.Contains("시", StringComparison.Ordinal);
    }

    private static bool HasExplicitOffset(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.EndsWith("Z", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var separatorIndex = Math.Max(trimmed.LastIndexOf('T'), trimmed.LastIndexOf(' '));
        if (separatorIndex < 0 || separatorIndex == trimmed.Length - 1)
        {
            return false;
        }

        var timePart = trimmed[(separatorIndex + 1)..];
        return timePart.Contains('+') || timePart.LastIndexOf('-') > 0;
    }

    private static DateTime EnsureUtc(DateTime value)
    {
        return value.Kind == DateTimeKind.Utc
            ? value
            : DateTime.SpecifyKind(value, DateTimeKind.Utc);
    }

    private static string BuildAppointmentReference(AppointmentData appointment)
    {
        return BuildAppointmentReference(appointment.GuildId, appointment.ChannelId, appointment.SourceMessageId);
    }

    private static string BuildAppointmentReference(string? guildId, string channelId, string? messageId)
    {
        if (string.IsNullOrWhiteSpace(messageId))
        {
            return "(원본 메시지를 참조할 수 없어요)";
        }

        var guildPart = string.IsNullOrWhiteSpace(guildId) ? "@me" : guildId;
        return $"https://discord.com/channels/{guildPart}/{channelId}/{messageId}";
    }
}
