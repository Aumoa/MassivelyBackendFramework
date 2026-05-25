using System.Text;
using System.Text.RegularExpressions;
using AI;
using DiscordBot.Services;

namespace DiscordBot.Games.Othello;

internal sealed partial class LlmOthelloOpponent(
    IChatClient chatClient,
    IClaudeSettingsService claudeSettings,
    IOthelloEngine engine,
    ILogger<LlmOthelloOpponent> logger) : IOthelloOpponent
{
    public async ValueTask<OthelloMoveInfo?> ChooseMoveAsync(
        OthelloGameSession session,
        IReadOnlyList<OthelloMoveInfo> legalMoves,
        CancellationToken cancellationToken = default)
    {
        if (legalMoves.Count == 0)
        {
            return null;
        }

        var evaluations = legalMoves.ToDictionary(
            move => move.Coordinate,
            move => EvaluateMove(session, move),
            StringComparer.OrdinalIgnoreCase);
        var prompt = BuildPrompt(session, legalMoves, evaluations);

        try
        {
            var settings = await claudeSettings.GetAsync(cancellationToken);
            var options = new ChatCompletionOptions
            {
                Model = settings.SummaryModel,
                Temperature = ResolveTemperature(session.Difficulty),
                MaxTokens = Math.Min(settings.DefaultMaxTokens, 256),
                ContextLength = 8192
            };

            var response = await chatClient.GenerateAsync(
                prompt,
                options,
                BuildSystemPrompt(session),
                cancellationToken);

            if (TryResolveMove(response, legalMoves, out var move))
            {
                return move;
            }

            logger.LogWarning("LLM othello opponent returned invalid move. Response: {Response}", response);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning(e, "Failed to choose LLM othello move. Falling back to heuristic move.");
        }

        return PickFallbackMove(session, legalMoves, evaluations);
    }

    private static string BuildSystemPrompt(OthelloGameSession session)
    {
        return $"""
너는 Discord 오셀로 게임의 AI 상대다.
너는 합법수 목록 중에서 정확히 하나의 좌표만 선택해야 한다.
합법수 목록 밖의 수, 설명, 코드 블록, 마크다운은 출력하지 마라.
오셀로 실력은 난이도와 성격에 맞추되, 사람처럼 가끔 불완전한 판단을 할 수 있다.
최종 출력은 좌표 하나만 사용한다. 예: d3, c4, f5

난이도: {session.Difficulty}
성격: {session.AiPersonality}
""";
    }

    private string BuildPrompt(
        OthelloGameSession session,
        IReadOnlyList<OthelloMoveInfo> legalMoves,
        IReadOnlyDictionary<string, int> evaluations)
    {
        var (blackCount, whiteCount) = engine.CountDiscs(session.Board);
        var builder = new StringBuilder();
        builder.AppendLine("[현재 오셀로 상태]");
        builder.AppendLine($"턴: {SideName(session.CurrentSide)}");
        builder.AppendLine($"흑: {session.Black.DisplayName}");
        builder.AppendLine($"백: {session.White.DisplayName}");
        builder.AppendLine($"점수: 흑 {blackCount}, 백 {whiteCount}");
        builder.AppendLine();
        builder.AppendLine("[보드]");
        builder.AppendLine(engine.ToPositionString(session.Board));

        if (session.MoveHistory.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("[최근 수]");
            foreach (var move in session.MoveHistory.TakeLast(10))
            {
                builder.AppendLine($"- {move.Number}. {SideName(move.Side)} {(move.IsPass ? "pass" : move.Coordinate)}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("[합법수 목록]");
        foreach (var move in legalMoves)
        {
            evaluations.TryGetValue(move.Coordinate, out var evaluation);
            builder.Append("- ").Append(move.Coordinate)
                .Append(" / flips=").Append(move.FlipCount)
                .Append(" / eval=").Append(evaluation);
            if (move.IsCorner) builder.Append(" / corner");
            if (move.IsEdge) builder.Append(" / edge");
            builder.AppendLine();
        }

        builder.AppendLine();
        builder.AppendLine("위 합법수 중 하나만 좌표로 출력해라.");
        return builder.ToString();
    }

    private int EvaluateMove(OthelloGameSession session, OthelloMoveInfo move)
    {
        var score = move.FlipCount * 4;
        var (file, rank) = FromCoordinate(move.Coordinate);

        if (move.IsCorner)
        {
            score += 120;
        }
        else if (IsDangerSquareNearEmptyCorner(session.Board, file, rank))
        {
            score -= 55;
        }
        else if (move.IsEdge)
        {
            score += 18;
        }

        var clone = session.Board.Clone();
        if (engine.TryApplyMove(clone, move.Coordinate, out _, out _))
        {
            score -= engine.GetLegalMoves(clone).Count * 3;
        }

        if (IsNearOwnedCorner(session.Board, session.CurrentSide, file, rank))
        {
            score += 12;
        }

        return score;
    }

    private static OthelloMoveInfo PickFallbackMove(
        OthelloGameSession session,
        IReadOnlyList<OthelloMoveInfo> legalMoves,
        IReadOnlyDictionary<string, int> evaluations)
    {
        var ranked = legalMoves
            .OrderByDescending(move => evaluations.TryGetValue(move.Coordinate, out var score) ? score : 0)
            .ThenByDescending(move => move.FlipCount)
            .ToList();

        return NormalizeDifficulty(session.Difficulty) switch
        {
            "easy" or "쉬움" => PickFrom(ranked.Skip(ranked.Count / 3).DefaultIfEmpty(ranked[^1]).ToList()),
            "hard" or "difficult" or "어려움" => PickFrom(ranked.Take(Math.Min(2, ranked.Count)).ToList()),
            _ => PickFrom(ranked.Take(Math.Max(1, Math.Min(4, ranked.Count))).ToList())
        };
    }

    private static OthelloMoveInfo PickFrom(IReadOnlyList<OthelloMoveInfo> moves)
    {
        return moves[Random.Shared.Next(moves.Count)];
    }

    private static bool TryResolveMove(
        string response,
        IReadOnlyList<OthelloMoveInfo> legalMoves,
        out OthelloMoveInfo? move)
    {
        move = null;
        var match = CoordinateRegex().Match(response.Trim());
        if (!match.Success)
        {
            return false;
        }

        var coordinate = match.Value.ToLowerInvariant();
        move = legalMoves.FirstOrDefault(m => string.Equals(m.Coordinate, coordinate, StringComparison.OrdinalIgnoreCase));
        return move != null;
    }

    private static bool IsDangerSquareNearEmptyCorner(OthelloBoard board, int file, int rank)
    {
        return IsNearCorner(file, rank, 0, 0) && board.Cells[0, 0] == OthelloDisc.Empty
            || IsNearCorner(file, rank, 7, 0) && board.Cells[7, 0] == OthelloDisc.Empty
            || IsNearCorner(file, rank, 0, 7) && board.Cells[0, 7] == OthelloDisc.Empty
            || IsNearCorner(file, rank, 7, 7) && board.Cells[7, 7] == OthelloDisc.Empty;
    }

    private static bool IsNearOwnedCorner(OthelloBoard board, OthelloSide side, int file, int rank)
    {
        var ownDisc = side == OthelloSide.Black ? OthelloDisc.Black : OthelloDisc.White;
        return IsNearCorner(file, rank, 0, 0) && board.Cells[0, 0] == ownDisc
            || IsNearCorner(file, rank, 7, 0) && board.Cells[7, 0] == ownDisc
            || IsNearCorner(file, rank, 0, 7) && board.Cells[0, 7] == ownDisc
            || IsNearCorner(file, rank, 7, 7) && board.Cells[7, 7] == ownDisc;
    }

    private static bool IsNearCorner(int file, int rank, int cornerFile, int cornerRank)
    {
        return Math.Abs(file - cornerFile) <= 1
            && Math.Abs(rank - cornerRank) <= 1
            && (file != cornerFile || rank != cornerRank);
    }

    private static (int File, int Rank) FromCoordinate(string coordinate) => (coordinate[0] - 'a', coordinate[1] - '1');

    private static float ResolveTemperature(string difficulty)
    {
        return NormalizeDifficulty(difficulty) switch
        {
            "easy" or "쉬움" => 0.9f,
            "hard" or "difficult" or "어려움" => 0.45f,
            _ => 0.7f
        };
    }

    private static string NormalizeDifficulty(string difficulty) => difficulty.Trim().ToLowerInvariant();

    private static string SideName(OthelloSide side) => side == OthelloSide.Black ? "흑" : "백";

    [GeneratedRegex(@"[a-h][1-8]", RegexOptions.IgnoreCase)]
    private static partial Regex CoordinateRegex();
}
