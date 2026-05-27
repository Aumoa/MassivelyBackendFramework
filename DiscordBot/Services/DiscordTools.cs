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
            lines.Add($"Time: {localTime:yyyy-MM-dd HH:mm:ss}");
            lines.Add($"Author: {author}");
            lines.Add($"Link: {reference}");
            lines.Add($"Content: {log.Content}");
            lines.Add("");
            index++;
        }

        return string.Join("\n", lines);
    }

    private static string BuildMessageReference(ChatLogData log)
    {
        if (string.IsNullOrEmpty(log.MessageId))
            return "(메시지가 오래되어 참조할 수 없어요)";

        var guildPart = string.IsNullOrEmpty(log.GuildId) ? "@me" : log.GuildId;
        return $"https://discord.com/channels/{guildPart}/{log.ChannelId}/{log.MessageId}";
    }

    [ToolFunction(
        Name = "remember_appointment",
        Description = """
사용자의 약속 또는 일정을 기억해야 할 때 사용합니다. 사용자가 '약속 기억해줘', '내일 3시 회의 저장해줘', '다음 주 금요일 저녁 약속 잊지 마'처럼 말하면 호출하세요.

[중요]
- starts_at에는 반드시 사용자의 자연어 날짜를 해석한 정식 날짜/시간을 ISO 형식으로 넣으세요. 예: 2026-05-28T15:00:00
- '내일', '다음 주 금요일', '저녁'처럼 상대적이거나 애매한 표현은 현재 날짜를 기준으로 해석하세요. 현재 날짜를 모르면 먼저 get_current_date를 호출하세요.
- 날짜나 시간이 불분명하면 이 도구를 호출하지 말고 사용자에게 정확한 날짜/시간을 물어보세요.
- 약속 저장은 사용자 본인 전용입니다. user_id, guild_id, channel_id는 프로그램이 현재 메시지에서 자동으로 고정합니다.
- 저장된 약속에는 원본 Discord 메시지 링크가 함께 보존됩니다. 조회 응답에는 해당 링크를 그대로 보여주세요.
- 지난 약속은 기본적으로 약속 시간 30일 뒤 자동으로 잊어버립니다.
""")]
    public async Task<string> RememberAppointmentAsync(
        [ToolParameterInfo(Description = "약속 제목. 짧고 구체적으로 작성하세요. 예: 치과 예약, 프로젝트 회의")]
        string title,
        [ToolParameterInfo(Description = "약속 시작 날짜/시간. ISO 형식 권장. 예: 2026-05-28T15:00:00 또는 2026-05-28T15:00:00+09:00")]
        string starts_at,
        [ToolParameterInfo(Description = "IANA 타임존 ID. 한국어 사용자의 기본값은 Asia/Seoul입니다.")]
        string timezone = "Asia/Seoul",
        [ToolParameterInfo(Description = "약속에 대한 추가 설명. 없으면 빈 문자열.")]
        string description = "",
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
        if (!TryParseAppointmentDateTime(starts_at, tz, out var startsAtUtc, out var parseError))
        {
            return parseError;
        }

        var nowUtc = DateTime.UtcNow;
        if (startsAtUtc < nowUtc.AddMinutes(-5))
        {
            return "이미 지난 시간으로 보입니다. 앞으로 있을 약속의 날짜와 시간을 다시 확인해 주세요.";
        }

        forget_after_days = Math.Clamp(forget_after_days, 1, MaxAppointmentRetentionDays);
        var expiresAtUtc = startsAtUtc.AddDays(forget_after_days);
        var guildId = (message.Channel as SocketGuildChannel)?.Guild.Id.ToString();
        var input = new AppointmentInput(
            guildId,
            message.Channel.Id.ToString(),
            message.Author.Id.ToString(),
            message.Id.ToString(),
            normalizedTitle,
            NormalizeNullable(description, 2048),
            startsAtUtc,
            tz.Id,
            expiresAtUtc);

        await appointmentRepository.ExpireOldAsync(nowUtc, cancellationToken);
        var id = await appointmentRepository.AddAsync(input, cancellationToken);
        var localStart = TimeZoneInfo.ConvertTimeFromUtc(startsAtUtc, tz);
        var localExpire = TimeZoneInfo.ConvertTimeFromUtc(expiresAtUtc, tz);
        var reference = BuildAppointmentReference(guildId, message.Channel.Id.ToString(), message.Id.ToString());

        return $"""
약속을 저장했습니다.
ID: {id}
제목: {normalizedTitle}
일시: {localStart:yyyy-MM-dd HH:mm:ss} ({tz.Id})
원본: {reference}
잊는 시점: {localExpire:yyyy-MM-dd HH:mm:ss} ({tz.Id})
응답 규칙: 사용자에게 약속을 기억했다고 짧게 알려주고, 날짜/시간과 원본 메시지 링크를 함께 확인해 주세요. 원본 URL은 Discord 인용 카드가 뜨도록 그대로 적으세요.
""";
    }

    [ToolFunction(
        Name = "list_appointments",
        Description = """
현재 사용자 본인이 저장한 약속을 조회합니다. 사용자가 '내 약속 알려줘', '이번 주 약속 뭐 있어?', '내일 일정 알려줘'처럼 말하면 호출하세요.

조회 범위는 현재 Discord 서버 또는 DM 범위로 제한됩니다. 다른 사용자나 다른 서버의 약속은 조회할 수 없습니다.
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
        await appointmentRepository.ExpireOldAsync(nowUtc, cancellationToken);
        var appointments = await appointmentRepository.GetActiveAsync(
            message.Author.Id.ToString(),
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
            var localStart = TimeZoneInfo.ConvertTimeFromUtc(EnsureUtc(appointment.StartsAtUtc), appointmentTz);
            var reference = BuildAppointmentReference(appointment);
            lines.Add("");
            lines.Add($"ID: {appointment.Id}");
            lines.Add($"제목: {appointment.Title}");
            lines.Add($"일시: {localStart:yyyy-MM-dd HH:mm:ss} ({appointmentTz.Id})");
            if (!string.IsNullOrWhiteSpace(appointment.Description))
            {
                lines.Add($"설명: {appointment.Description}");
            }
            lines.Add($"원본: {reference}");
        }

        lines.Add("");
        lines.Add("응답 규칙: 사용자에게 필요한 약속만 간결하게 정리하세요. 원본 URL은 Discord 인용 카드가 뜨도록 그대로 적으세요. 삭제가 필요하면 ID를 기준으로 forget_appointment를 사용할 수 있습니다.");
        return string.Join("\n", lines);
    }

    [ToolFunction(
        Name = "forget_appointment",
        Description = """
사용자가 저장된 약속을 삭제하거나 잊어달라고 요청할 때 사용합니다.
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
        await appointmentRepository.ExpireOldAsync(nowUtc, cancellationToken);
        var appointment = await appointmentRepository.GetActiveByIdAsync(
            id,
            message.Author.Id.ToString(),
            guildId,
            nowUtc,
            cancellationToken);
        if (appointment == null)
        {
            return "해당 ID의 활성 약속을 찾지 못했습니다.";
        }

        var deleted = await appointmentRepository.DeleteAsync(
            id,
            message.Author.Id.ToString(),
            guildId,
            cancellationToken);
        if (!deleted)
        {
            return "약속 삭제에 실패했습니다. 이미 삭제되었거나 찾을 수 없습니다.";
        }

        var appointmentTz = ResolveTimeZone(appointment.Timezone);
        var localStart = TimeZoneInfo.ConvertTimeFromUtc(EnsureUtc(appointment.StartsAtUtc), appointmentTz);
        return $"""
약속을 삭제했습니다.
ID: {appointment.Id}
제목: {appointment.Title}
일시: {localStart:yyyy-MM-dd HH:mm:ss} ({appointmentTz.Id})
삭제 이유: {(string.IsNullOrWhiteSpace(reason) ? "미지정" : reason)}
응답 규칙: 사용자에게 삭제 완료를 짧게 알려주세요.
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

        if (!TryParseAppointmentDateTime(dateString, timezone, out var parsed, out error))
        {
            return false;
        }

        utcDateTime = parsed;
        return true;
    }

    private static bool TryParseAppointmentDateTime(
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
