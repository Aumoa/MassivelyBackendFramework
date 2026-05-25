using System.Text;
using System.Text.RegularExpressions;
using AI;
using DiscordBot.Services;

namespace DiscordBot.Games.Chess;

internal sealed partial class LlmChessOpponent(
    IChatClient chatClient,
    IClaudeSettingsService claudeSettings,
    IChessEvaluationProvider evaluationProvider,
    ILogger<LlmChessOpponent> logger) : IChessOpponent
{
    public async ValueTask<ChessMoveInfo?> ChooseMoveAsync(
        ChessGameSession session,
        IReadOnlyList<ChessMoveInfo> legalMoves,
        CancellationToken cancellationToken = default)
    {
        if (legalMoves.Count == 0)
        {
            return null;
        }

        var evaluations = await evaluationProvider.EvaluateAsync(session, legalMoves, cancellationToken);
        var prompt = BuildPrompt(session, legalMoves, evaluations);
        try
        {
            var settings = await claudeSettings.GetAsync(cancellationToken);
            var options = new ChatCompletionOptions
            {
                Model = settings.SummaryModel,
                Temperature = ResolveTemperature(session.Difficulty),
                MaxTokens = Math.Min(settings.DefaultMaxTokens, 1024),
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

            logger.LogWarning("LLM chess opponent returned invalid move. Response: {Response}", response);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning(e, "Failed to choose LLM chess move. Falling back to a random legal move.");
        }

        return PickFallbackMove(session, legalMoves);
    }

    private static string BuildSystemPrompt(ChessGameSession session)
    {
        return $"""
너는 Discord 체스 게임의 AI 상대다.
너는 합법수 목록 중에서 정확히 하나의 수만 선택해야 한다.
합법수 목록 밖의 수, 설명, 코드 블록, 마크다운은 출력하지 마라.
체스 실력은 난이도와 성격에 맞추되, 사람처럼 가끔 불완전한 판단을 할 수 있다.
최종 출력은 UCI 형식의 move 하나만 사용한다. 예: e2e4, g1f3, e7e8q

난이도: {session.Difficulty}
성격: {session.AiPersonality}
""";
    }

    private static string BuildPrompt(
        ChessGameSession session,
        IReadOnlyList<ChessMoveInfo> legalMoves,
        IReadOnlyDictionary<string, ChessMoveEvaluation> evaluations)
    {
        var builder = new StringBuilder();
        builder.AppendLine("[현재 체스 상태]");
        builder.AppendLine($"FEN: {session.Board.ToFen()}");
        builder.AppendLine($"턴: {SideName(session.CurrentSide)}");
        builder.AppendLine($"백: {session.White.DisplayName}");
        builder.AppendLine($"흑: {session.Black.DisplayName}");

        if (session.MoveHistory.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("[최근 수]");
            foreach (var move in session.MoveHistory.TakeLast(8))
            {
                builder.AppendLine($"- {move.Number}. {SideName(move.Side)} {move.San ?? move.Uci}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("[합법수 목록]");
        foreach (var move in legalMoves)
        {
            evaluations.TryGetValue(move.Uci, out var evaluation);
            builder.Append("- ").Append(move.Uci);
            if (!string.IsNullOrWhiteSpace(move.San))
            {
                builder.Append(" / SAN ").Append(move.San);
            }

            builder.Append(" / ").Append(PieceName(move.Piece));
            if (move.IsCapture) builder.Append(" / capture");
            if (move.IsCastle) builder.Append(" / castle");
            if (move.IsEnPassant) builder.Append(" / en-passant");
            if (move.Promotion != null) builder.Append(" / promotion=").Append(move.Promotion);
            if (move.IsCheck) builder.Append(" / check");
            if (move.IsMate) builder.Append(" / mate");
            if (evaluation != null)
            {
                builder.Append(" / eval=").Append(evaluation.Label);
                if (evaluation.CentipawnScore.HasValue)
                {
                    builder.Append('(').Append(evaluation.CentipawnScore.Value).Append("cp)");
                }
            }

            builder.AppendLine();
        }

        builder.AppendLine();
        builder.AppendLine("위 합법수 중 하나만 UCI 형식으로 출력해라.");
        return builder.ToString();
    }

    private static bool TryResolveMove(
        string response,
        IReadOnlyList<ChessMoveInfo> legalMoves,
        out ChessMoveInfo? move)
    {
        move = null;
        var normalized = response.Trim();
        var match = UciMoveRegex().Match(normalized);
        if (match.Success)
        {
            var uci = match.Value.ToLowerInvariant();
            move = legalMoves.FirstOrDefault(m => string.Equals(m.Uci, uci, StringComparison.OrdinalIgnoreCase));
            return move != null;
        }

        move = legalMoves.FirstOrDefault(m =>
            !string.IsNullOrWhiteSpace(m.San)
            && string.Equals(m.San, normalized, StringComparison.OrdinalIgnoreCase));
        return move != null;
    }

    private static ChessMoveInfo PickFallbackMove(ChessGameSession session, IReadOnlyList<ChessMoveInfo> legalMoves)
    {
        var difficulty = NormalizeDifficulty(session.Difficulty);
        var tacticalMoves = legalMoves
            .Where(move => move.IsMate || move.IsCheck || move.IsCapture || move.IsCastle)
            .ToList();

        if (difficulty is "hard" or "difficult" or "어려움" && tacticalMoves.Count > 0)
        {
            return tacticalMoves[Random.Shared.Next(tacticalMoves.Count)];
        }

        return legalMoves[Random.Shared.Next(legalMoves.Count)];
    }

    private static float ResolveTemperature(string difficulty)
    {
        return NormalizeDifficulty(difficulty) switch
        {
            "easy" or "쉬움" => 0.95f,
            "hard" or "difficult" or "어려움" => 0.45f,
            _ => 0.75f
        };
    }

    private static string NormalizeDifficulty(string difficulty) => difficulty.Trim().ToLowerInvariant();

    private static string SideName(ChessSide side) => side == ChessSide.White ? "백" : "흑";

    private static string PieceName(string piece)
    {
        if (piece.Length < 2)
        {
            return "unknown";
        }

        var side = piece[0] == 'w' ? "white" : "black";
        var name = piece[1] switch
        {
            'p' => "pawn",
            'n' => "knight",
            'b' => "bishop",
            'r' => "rook",
            'q' => "queen",
            'k' => "king",
            _ => "piece"
        };
        return $"{side} {name}";
    }

    [GeneratedRegex(@"[a-h][1-8][a-h][1-8][qrbn]?", RegexOptions.IgnoreCase)]
    private static partial Regex UciMoveRegex();
}
