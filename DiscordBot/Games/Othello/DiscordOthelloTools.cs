using System.Text.RegularExpressions;
using AI;
using Discord;
using Discord.WebSocket;
using DiscordBot.Games.Chess;
using DiscordBot.Repositories;

namespace DiscordBot.Games.Othello;

internal sealed partial class DiscordOthelloTools(
    SocketSelfUser selfUser,
    SocketMessage message,
    IOthelloGameService othelloGameService,
    IChessGameService chessGameService,
    IChatLogRepository chatLogRepository,
    ILogger<DiscordOthelloTools> logger) : IToolFunctionDescriptionProvider
{
    private const string PlayOthelloDescription = """
오셀로 게임을 시작합니다. 오셀로 또는 리버시 게임 시작 요청에만 사용하세요.

black_player와 white_player에는 'me', 'ai', Discord 멘션(<@id>), 또는 현재 채널에서 볼 수 있는 사용자 이름을 넣으세요.
사람 이름을 확실히 찾을 수 없으면 도구 결과가 사용자에게 정확한 멘션을 요청합니다.
AI 상대와 두는 경우 한쪽 플레이어를 'ai'로 지정하세요.
오셀로는 흑이 선공입니다.

예:
- 사용자가 '나랑 오셀로 하자'라고 하면 black_player='me', white_player='ai'
- 사용자가 '내가 백으로 AI랑 둘래'라고 하면 black_player='ai', white_player='me'
- 사용자가 '@철수랑 오셀로하고 싶어'라고 하면 black_player='me', white_player='<@철수 id>'
""";

    private const string MoveOthelloBaseDescription = """
진행 중인 오셀로 게임에서 돌을 둡니다.

move에는 가능한 한 좌표 표기(d3, c4, f5)를 넣으세요.
사용자가 '왼쪽 위 구석', '가장 많이 뒤집는 곳', '안전한 곳', '오른쪽 아래'처럼 자연어로 말하면 현재 합법수와 보드 상태를 참고해 가장 구체적인 좌표로 바꿔 넣으세요.
보드 이미지는 항상 백 기준 좌표입니다. 왼쪽 아래는 a1, 오른쪽 아래는 h1, 왼쪽 위는 a8입니다. 사용자의 '왼쪽/오른쪽/위/아래' 표현도 이 화면 기준으로 해석하세요.

합법수가 애매하면 억지로 실행하지 말고 사용자에게 어느 칸에 둘지 되물으세요.
도구 결과에 상태가 game_over로 표시되면 오셀로 세션은 이미 종료된 것입니다. 종료 요약을 그대로 전달하고 새 게임 권유, 칭찬, 다음 수 안내를 덧붙이지 마세요.
""";

    [ToolFunction(
        Name = "play_othello",
        Description = PlayOthelloDescription)]
    public async Task<string> PlayOthelloAsync(
        [ToolParameterInfo(Description = "흑 플레이어. 'me', 'ai', Discord 멘션, 또는 현재 채널에서 볼 수 있는 사용자 이름. 흑이 선공입니다.")]
        string black_player = "me",
        [ToolParameterInfo(Description = "백 플레이어. 'me', 'ai', Discord 멘션, 또는 현재 채널에서 볼 수 있는 사용자 이름.")]
        string white_player = "ai",
        [ToolParameterInfo(Description = "AI 난이도. easy, normal, hard 또는 한국어 쉬움/보통/어려움. 기본값 normal.")]
        string difficulty = "normal",
        [ToolParameterInfo(Description = "AI 플레이 성향. 예: balanced, aggressive, defensive, beginner-like. 기본값 balanced.")]
        string ai_personality = "balanced",
        CancellationToken cancellationToken = default)
    {
        var black = ResolveParticipant(black_player, defaultToAuthor: true);
        if (!black.Success)
        {
            return black.ErrorMessage;
        }

        var white = ResolveParticipant(white_player, defaultToAuthor: false);
        if (!white.Success)
        {
            return white.ErrorMessage;
        }

        var conflict = ValidateNoActiveChessGame(black.Participant!, white.Participant!);
        if (conflict != null)
        {
            return conflict;
        }

        var guildId = (message.Channel as SocketGuildChannel)?.Guild.Id.ToString();
        var result = await othelloGameService.StartAsync(
            guildId,
            message.Channel.Id.ToString(),
            message.Author.Id.ToString(),
            black.Participant!,
            white.Participant!,
            difficulty,
            ai_personality,
            cancellationToken);

        return await SendBoardAndReturnMessageAsync(result);
    }

    [ToolFunction(
        Name = "move_othello",
        Description = MoveOthelloBaseDescription)]
    public async Task<string> MoveOthelloAsync(
        [ToolParameterInfo(Description = "돌을 둘 위치. 가능하면 좌표(d3/c4/f5), 또는 자연어 위치 설명.")]
        string move,
        CancellationToken cancellationToken = default)
    {
        var result = await othelloGameService.MoveAsync(
            message.Author.Id.ToString(),
            message.Channel.Id.ToString(),
            move,
            cancellationToken);

        return await SendBoardAndReturnMessageAsync(result);
    }

    [ToolFunction(
        Name = "pass_othello",
        Description = "진행 중인 오셀로 게임에서 패스합니다. 사용자가 '둘 곳 없다', '패스', '넘길게'처럼 말하면 호출하세요. 단, 합법수가 없을 때만 성공합니다.")]
    public async Task<string> PassOthelloAsync(CancellationToken cancellationToken = default)
    {
        var result = await othelloGameService.PassAsync(
            message.Author.Id.ToString(),
            message.Channel.Id.ToString(),
            cancellationToken);

        return await SendBoardAndReturnMessageAsync(result);
    }

    [ToolFunction(
        Name = "surrender_othello",
        Description = "진행 중인 오셀로 게임을 기권하고 종료합니다. 사용자가 '졌다', '그만하자', '항복', '끝내자', '내가 진 것 같아'처럼 말하면 이 도구를 호출하세요.")]
    public async Task<string> SurrenderOthelloAsync(
        [ToolParameterInfo(Description = "사용자가 말한 기권/종료 이유. 없으면 빈 문자열.")]
        string reason = "",
        CancellationToken cancellationToken = default)
    {
        var result = await othelloGameService.SurrenderAsync(
            message.Author.Id.ToString(),
            message.Channel.Id.ToString(),
            reason,
            cancellationToken);

        return await SendBoardAndReturnMessageAsync(result);
    }

    [ToolFunction(
        Name = "show_othello",
        Description = "진행 중인 오셀로 게임의 현재 보드를 다시 이미지로 보여줍니다.")]
    public async Task<string> ShowOthelloAsync(CancellationToken cancellationToken = default)
    {
        var result = await othelloGameService.ShowAsync(
            message.Author.Id.ToString(),
            message.Channel.Id.ToString(),
            cancellationToken);

        return await SendBoardAndReturnMessageAsync(result);
    }

    public string? GetToolFunctionDescription(string functionName)
    {
        return functionName switch
        {
            "move_othello" => MoveOthelloBaseDescription + "\n\n" + othelloGameService.BuildActiveGameInstruction(
                message.Author.Id.ToString(),
                message.Channel.Id.ToString()),
            "pass_othello" => "진행 중인 오셀로 게임에서 현재 플레이어가 둘 수 있는 합법수가 없을 때만 패스합니다.\n\n"
                + othelloGameService.BuildActiveGameInstruction(message.Author.Id.ToString(), message.Channel.Id.ToString()),
            "play_othello" => PlayOthelloDescription,
            _ => null
        };
    }

    private async Task<string> SendBoardAndReturnMessageAsync(OthelloGameActionResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.SystemNotice))
        {
            await SendSystemNoticeAsync(result.SystemNoticeTitle, result.SystemNotice);
        }

        if (result.BoardImage is { Length: > 0 })
        {
            await using var stream = new MemoryStream(result.BoardImage, writable: false);
            await message.Channel.SendFileAsync(stream, result.FileName ?? "othello.png");
        }

        var authoritativeState = BuildAuthoritativeState();
        if (result.IsGameOver)
        {
            await SendSystemNoticeAsync("오셀로 게임 요약", result.Message);

            return $"""
[오셀로 도구 결과]
상태: game_over
시스템 알림: 이미 별도 메시지로 전송되었습니다.
종료 요약: 이미 별도 메시지로 전송되었습니다.
응답 규칙: 현재 오셀로 세션은 이미 종료되었습니다. 사용자에게는 처리 완료 여부만 한 문장으로 짧게 말하세요. 종료 요약, 새 게임 권유, 칭찬, 다음 수 안내, 추가 해설을 덧붙이지 마세요.

{result.Message}
""";
        }

        return $"""
[오셀로 도구 결과]
상태: {(result.Success ? "active" : "error")}
시스템 알림: {(string.IsNullOrWhiteSpace(result.SystemNotice) ? "없음" : "이미 별도 메시지로 전송되었습니다.")}
응답 규칙: 아래 내용을 간결하게 전달하세요. 보드 상태와 합법수는 이전 대화나 추론보다 [권위 상태]를 우선하세요. 추천/후보/선택은 [권위 상태]의 현재 합법수 목록 안에서만 말하세요.

{result.Message}

[권위 상태]
{authoritativeState}
""";
    }

    private string BuildAuthoritativeState()
    {
        var state = othelloGameService.BuildActiveGameInstruction(
            message.Author.Id.ToString(),
            message.Channel.Id.ToString());

        return string.IsNullOrWhiteSpace(state)
            ? "현재 활성 오셀로 상태를 찾지 못했습니다."
            : state.Trim();
    }

    private async Task SendSystemNoticeAsync(string? title, string notice)
    {
        var content = BuildSystemNoticeMessage(title, notice);
        var sentMessage = await message.Channel.SendMessageAsync(content);

        try
        {
            var guildId = (message.Channel as SocketGuildChannel)?.Guild.Id.ToString();
            await chatLogRepository.AddAsync(
                sentMessage.Id.ToString(),
                guildId,
                message.Channel.Id.ToString(),
                selfUser.Id.ToString(),
                content);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Failed to save othello system notice.");
        }
    }

    private static string BuildSystemNoticeMessage(string? title, string notice)
    {
        var normalizedTitle = string.IsNullOrWhiteSpace(title)
            ? "오셀로 알림"
            : title.Trim();

        var lines = notice
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => $"> {line}");

        return $"> **{normalizedTitle}**\n" + string.Join("\n", lines);
    }

    private string? ValidateNoActiveChessGame(params OthelloParticipant[] participants)
    {
        foreach (var participant in participants)
        {
            if (participant.IsAi || string.IsNullOrWhiteSpace(participant.UserId))
            {
                continue;
            }

            if (chessGameService.FindActiveByUser(participant.UserId) != null)
            {
                return $"{participant.DisplayName}님은 이미 진행 중인 체스 게임이 있습니다. 먼저 해당 게임을 종료해 주세요.";
            }
        }

        return null;
    }

    private ParticipantResolution ResolveParticipant(string rawValue, bool defaultToAuthor)
    {
        var raw = NormalizeInput(rawValue);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return defaultToAuthor
                ? ParticipantResolution.Ok(OthelloParticipant.Human(DisplayName(message.Author), message.Author.Id.ToString()))
                : ParticipantResolution.Ok(OthelloParticipant.Ai());
        }

        if (IsAiValue(raw))
        {
            return ParticipantResolution.Ok(OthelloParticipant.Ai(selfUser.Username));
        }

        if (IsAuthorValue(raw))
        {
            return ParticipantResolution.Ok(OthelloParticipant.Human(DisplayName(message.Author), message.Author.Id.ToString()));
        }

        var mentionId = ExtractMentionId(raw);
        if (mentionId != null)
        {
            if (mentionId == selfUser.Id)
            {
                return ParticipantResolution.Ok(OthelloParticipant.Ai(selfUser.Username));
            }

            var mentionedUser = message.MentionedUsers.FirstOrDefault(user => user.Id == mentionId.Value)
                ?? (message.Channel as SocketGuildChannel)?.Guild.GetUser(mentionId.Value);

            return mentionedUser == null
                ? ParticipantResolution.Fail("해당 사용자를 볼 수 없습니다. 정확히 멘션해서 다시 지정해 주세요.")
                : ParticipantResolution.Ok(OthelloParticipant.Human(DisplayName(mentionedUser), mentionedUser.Id.ToString()));
        }

        var matchedUsers = FindVisibleUsers(raw).ToList();
        if (matchedUsers.Count == 1)
        {
            var user = matchedUsers[0];
            if (user.Id == selfUser.Id)
            {
                return ParticipantResolution.Ok(OthelloParticipant.Ai(selfUser.Username));
            }

            return ParticipantResolution.Ok(OthelloParticipant.Human(DisplayName(user), user.Id.ToString()));
        }

        if (matchedUsers.Count > 1)
        {
            return ParticipantResolution.Fail("이름이 일치하는 사용자가 여러 명입니다. 정확히 태그해서 다시 지정해 주세요.");
        }

        return ParticipantResolution.Fail("현재 채널에서 해당 사용자를 찾지 못했습니다. 정확히 태그해서 다시 지정해 주세요.");
    }

    private IEnumerable<SocketUser> FindVisibleUsers(string raw)
    {
        var normalized = NormalizeName(raw);
        var users = GetVisibleUsers()
            .Where(user => !user.IsBot || user.Id == selfUser.Id)
            .DistinctBy(user => user.Id)
            .ToList();

        var exact = users
            .Where(user => NormalizeName(DisplayName(user)) == normalized
                || NormalizeName(user.Username) == normalized)
            .ToList();
        if (exact.Count > 0)
        {
            return exact;
        }

        return users.Where(user =>
            NormalizeName(DisplayName(user)).Contains(normalized, StringComparison.OrdinalIgnoreCase)
            || NormalizeName(user.Username).Contains(normalized, StringComparison.OrdinalIgnoreCase));
    }

    private IEnumerable<SocketUser> GetVisibleUsers()
    {
        if (message.Channel is SocketTextChannel textChannel)
        {
            foreach (var user in textChannel.Users)
            {
                yield return user;
            }
        }

        if (message.Channel is SocketGuildChannel guildChannel)
        {
            foreach (var user in guildChannel.Guild.Users)
            {
                yield return user;
            }
        }

        if (message.Channel is SocketDMChannel dmChannel)
        {
            yield return dmChannel.Recipient;
        }

        yield return message.Author;
    }

    private static string DisplayName(SocketUser user)
    {
        return user is SocketGuildUser guildUser && !string.IsNullOrWhiteSpace(guildUser.Nickname)
            ? guildUser.Nickname
            : user.Username;
    }

    private static string NormalizeInput(string value)
    {
        return (value ?? string.Empty).Trim().Trim('"', '\'', '`');
    }

    private static string NormalizeName(string value)
    {
        return NormalizeInput(value)
            .Replace(" ", string.Empty)
            .ToLowerInvariant();
    }

    private static bool IsAiValue(string value)
    {
        var normalized = NormalizeName(value);
        return normalized is "ai" or "bot" or "computer" or "cpu" or "assistant" or "claude" or "봇" or "인공지능" or "컴퓨터";
    }

    private static bool IsAuthorValue(string value)
    {
        var normalized = NormalizeName(value);
        return normalized is "me" or "myself" or "나" or "저" or "내" or "본인" or "사용자" or "human";
    }

    private static ulong? ExtractMentionId(string value)
    {
        var match = MentionRegex().Match(value);
        return match.Success && ulong.TryParse(match.Groups["id"].Value, out var id)
            ? id
            : null;
    }

    private sealed record ParticipantResolution(
        bool Success,
        OthelloParticipant? Participant,
        string ErrorMessage)
    {
        public static ParticipantResolution Ok(OthelloParticipant participant) => new(true, participant, string.Empty);

        public static ParticipantResolution Fail(string message) => new(false, null, message);
    }

    [GeneratedRegex(@"<@!?(?<id>\d+)>")]
    private static partial Regex MentionRegex();
}
