using System.Text.RegularExpressions;
using AI;
using Discord;
using Discord.WebSocket;
using DiscordBot.Repositories;

namespace DiscordBot.Games.Chess;

internal sealed partial class DiscordChessTools(
    SocketSelfUser selfUser,
    SocketMessage message,
    IChessGameService chessGameService,
    IChatLogRepository chatLogRepository,
    ILogger<DiscordChessTools> logger) : IToolFunctionDescriptionProvider
{
    private const string PlayChessDescription = """
체스 게임을 시작합니다. 체스 게임 시작 요청에만 사용하세요.

white_player와 black_player에는 'me', 'ai', Discord 멘션(<@id>), 또는 현재 채널에서 볼 수 있는 사용자 이름을 넣으세요.
사람 이름을 확실히 찾을 수 없으면 도구 결과가 사용자에게 정확한 멘션을 요청합니다.
AI 상대와 두는 경우 한쪽 플레이어를 'ai'로 지정하세요.

예:
- 사용자가 '나랑 체스하자'라고 하면 white_player='me', black_player='ai'
- 사용자가 '내가 흑으로 AI랑 둘래'라고 하면 white_player='ai', black_player='me'
- 사용자가 '@철수랑 체스하고 싶어'라고 하면 white_player='me', black_player='<@철수 id>'
""";

    private const string MoveChessBaseDescription = """
진행 중인 체스 게임에서 말을 이동합니다.

move에는 가능한 한 UCI 좌표 표기(e2e4, g1f3, e7e8q)를 넣으세요.
사용자가 SAN(Nf3, O-O, exd5 등)으로 말하면 그대로 넣어도 됩니다.
사용자가 '왼쪽 두 번째 폰 두 칸', '앙파상', '캐슬링'처럼 자연어로 말하면 현재 합법수와 보드 상태를 참고해 가장 구체적인 이동으로 바꿔 넣으세요.
보드 이미지는 항상 백 기준입니다. 왼쪽 아래는 a1, 오른쪽 아래는 h1, 왼쪽 위는 a8입니다. 사용자의 '왼쪽/오른쪽/위/아래' 표현도 이 화면 기준으로 해석하세요.

합법수가 애매하면 억지로 실행하지 말고 사용자에게 어느 말을 움직일지 되물으세요.
도구 결과에 상태가 game_over로 표시되면 체스 세션은 이미 종료된 것입니다. 종료 요약을 그대로 전달하고 새 게임 권유, 칭찬, 다음 수 안내를 덧붙이지 마세요.
""";

    [ToolFunction(
        Name = "play_chess",
        Description = PlayChessDescription)]
    public async Task<string> PlayChessAsync(
        [ToolParameterInfo(Description = "백 플레이어. 'me', 'ai', Discord 멘션, 또는 현재 채널에서 볼 수 있는 사용자 이름.")]
        string white_player = "me",
        [ToolParameterInfo(Description = "흑 플레이어. 'me', 'ai', Discord 멘션, 또는 현재 채널에서 볼 수 있는 사용자 이름.")]
        string black_player = "ai",
        [ToolParameterInfo(Description = "AI 난이도. easy, normal, hard 또는 한국어 쉬움/보통/어려움. 기본값 normal.")]
        string difficulty = "normal",
        [ToolParameterInfo(Description = "AI 플레이 성향. 예: balanced, aggressive, defensive, beginner-like. 기본값 balanced.")]
        string ai_personality = "balanced",
        CancellationToken cancellationToken = default)
    {
        var white = ResolveParticipant(white_player, defaultToAuthor: true);
        if (!white.Success)
        {
            return white.ErrorMessage;
        }

        var black = ResolveParticipant(black_player, defaultToAuthor: false);
        if (!black.Success)
        {
            return black.ErrorMessage;
        }

        var guildId = (message.Channel as SocketGuildChannel)?.Guild.Id.ToString();
        var result = await chessGameService.StartAsync(
            guildId,
            message.Channel.Id.ToString(),
            message.Author.Id.ToString(),
            white.Participant!,
            black.Participant!,
            difficulty,
            ai_personality,
            cancellationToken);

        return await SendBoardAndReturnMessageAsync(result);
    }

    [ToolFunction(
        Name = "move_chess",
        Description = MoveChessBaseDescription)]
    public async Task<string> MoveChessAsync(
        [ToolParameterInfo(Description = "이동할 수. 가능하면 UCI(e2e4), 또는 SAN(Nf3/O-O), 또는 자연어 이동 설명.")]
        string move,
        CancellationToken cancellationToken = default)
    {
        var result = await chessGameService.MoveAsync(
            message.Author.Id.ToString(),
            message.Channel.Id.ToString(),
            move,
            cancellationToken);

        return await SendBoardAndReturnMessageAsync(result);
    }

    [ToolFunction(
        Name = "surrender_chess",
        Description = "진행 중인 체스 게임을 기권하고 종료합니다. 사용자가 '졌다', '그만하자', '항복', '끝내자', '내가 진 것 같아'처럼 말하면 이 도구를 호출하세요.")]
    public async Task<string> SurrenderChessAsync(
        [ToolParameterInfo(Description = "사용자가 말한 기권/종료 이유. 없으면 빈 문자열.")]
        string reason = "",
        CancellationToken cancellationToken = default)
    {
        var result = await chessGameService.SurrenderAsync(
            message.Author.Id.ToString(),
            message.Channel.Id.ToString(),
            reason,
            cancellationToken);

        return await SendBoardAndReturnMessageAsync(result);
    }

    [ToolFunction(
        Name = "show_chess",
        Description = "진행 중인 체스 게임의 현재 보드를 다시 이미지로 보여줍니다.")]
    public async Task<string> ShowChessAsync(CancellationToken cancellationToken = default)
    {
        var result = await chessGameService.ShowAsync(
            message.Author.Id.ToString(),
            message.Channel.Id.ToString(),
            cancellationToken);

        return await SendBoardAndReturnMessageAsync(result);
    }

    public string? GetToolFunctionDescription(string functionName)
    {
        return functionName switch
        {
            "move_chess" => MoveChessBaseDescription + "\n\n" + chessGameService.BuildActiveGameInstruction(
                message.Author.Id.ToString(),
                message.Channel.Id.ToString()),
            "play_chess" => PlayChessDescription,
            _ => null
        };
    }

    private async Task<string> SendBoardAndReturnMessageAsync(ChessGameActionResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.SystemNotice))
        {
            await SendSystemNoticeAsync(result.SystemNoticeTitle, result.SystemNotice);
        }

        if (result.BoardImage is { Length: > 0 })
        {
            await using var stream = new MemoryStream(result.BoardImage, writable: false);
            await message.Channel.SendFileAsync(stream, result.FileName ?? "chess.png");
        }

        if (result.IsGameOver)
        {
            await SendSystemNoticeAsync("체스 게임 요약", result.Message);

            return $"""
[체스 도구 결과]
상태: game_over
시스템 알림: 이미 별도 메시지로 전송되었습니다.
종료 요약: 이미 별도 메시지로 전송되었습니다.
응답 규칙: 현재 체스 세션은 이미 종료되었습니다. 사용자에게는 처리 완료 여부만 한 문장으로 짧게 말하세요. 종료 요약, 새 게임 권유, 칭찬, 다음 수 안내, 추가 해설을 덧붙이지 마세요.

{result.Message}
""";
        }

        return $"""
[체스 도구 결과]
상태: {(result.Success ? "active" : "error")}
시스템 알림: {(string.IsNullOrWhiteSpace(result.SystemNotice) ? "없음" : "이미 별도 메시지로 전송되었습니다.")}
응답 규칙: 아래 내용을 간결하게 전달하세요.

{result.Message}
""";
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
            logger.LogWarning(e, "Failed to save chess system notice.");
        }
    }

    private static string BuildSystemNoticeMessage(string? title, string notice)
    {
        var normalizedTitle = string.IsNullOrWhiteSpace(title)
            ? "체스 알림"
            : title.Trim();

        var lines = notice
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => $"> {line}");

        return $"> **{normalizedTitle}**\n" + string.Join("\n", lines);
    }

    private ParticipantResolution ResolveParticipant(string rawValue, bool defaultToAuthor)
    {
        var raw = NormalizeInput(rawValue);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return defaultToAuthor
                ? ParticipantResolution.Ok(ChessParticipant.Human(DisplayName(message.Author), message.Author.Id.ToString()))
                : ParticipantResolution.Ok(ChessParticipant.Ai());
        }

        if (IsAiValue(raw))
        {
            return ParticipantResolution.Ok(ChessParticipant.Ai(selfUser.Username));
        }

        if (IsAuthorValue(raw))
        {
            return ParticipantResolution.Ok(ChessParticipant.Human(DisplayName(message.Author), message.Author.Id.ToString()));
        }

        var mentionId = ExtractMentionId(raw);
        if (mentionId != null)
        {
            if (mentionId == selfUser.Id)
            {
                return ParticipantResolution.Ok(ChessParticipant.Ai(selfUser.Username));
            }

            var mentionedUser = message.MentionedUsers.FirstOrDefault(user => user.Id == mentionId.Value)
                ?? (message.Channel as SocketGuildChannel)?.Guild.GetUser(mentionId.Value);

            return mentionedUser == null
                ? ParticipantResolution.Fail("해당 사용자를 볼 수 없습니다. 정확히 멘션해서 다시 지정해 주세요.")
                : ParticipantResolution.Ok(ChessParticipant.Human(DisplayName(mentionedUser), mentionedUser.Id.ToString()));
        }

        var matchedUsers = FindVisibleUsers(raw).ToList();
        if (matchedUsers.Count == 1)
        {
            var user = matchedUsers[0];
            if (user.Id == selfUser.Id)
            {
                return ParticipantResolution.Ok(ChessParticipant.Ai(selfUser.Username));
            }

            return ParticipantResolution.Ok(ChessParticipant.Human(DisplayName(user), user.Id.ToString()));
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
        ChessParticipant? Participant,
        string ErrorMessage)
    {
        public static ParticipantResolution Ok(ChessParticipant participant) => new(true, participant, string.Empty);

        public static ParticipantResolution Fail(string message) => new(false, null, message);
    }

    [GeneratedRegex(@"<@!?(?<id>\d+)>")]
    private static partial Regex MentionRegex();
}
