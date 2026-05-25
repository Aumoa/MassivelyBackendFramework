using System.Text.RegularExpressions;
using Chess;

namespace DiscordBot.Games.Chess;

internal sealed partial class GeraChessEngine : IChessEngine
{
    public ChessBoard CreateBoard() => new();

    public IReadOnlyList<ChessMoveInfo> GetLegalMoves(ChessBoard board)
    {
        return board.Moves(true, true)
            .Select(move => BuildMoveInfo(move))
            .ToList();
    }

    public bool TryApplyMove(ChessBoard board, string moveText, out ChessMoveInfo? appliedMove, out string errorMessage)
    {
        appliedMove = null;
        errorMessage = string.Empty;

        var normalized = NormalizeMoveText(moveText);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            errorMessage = "이동할 수를 알아듣지 못했습니다.";
            return false;
        }

        var legalMoves = GetLegalMoves(board);
        var matchingMove = ResolveMove(board, legalMoves, normalized, out errorMessage);
        if (matchingMove == null)
        {
            return false;
        }

        try
        {
            if (!board.Move(matchingMove.SourceMove))
            {
                errorMessage = "합법적인 수가 아닙니다.";
                return false;
            }

            appliedMove = matchingMove;
            return true;
        }
        catch (ChessException e)
        {
            errorMessage = e.Message;
            return false;
        }
    }

    public void Resign(ChessBoard board, ChessSide side)
    {
        board.Resign(side == ChessSide.White ? PieceColor.White : PieceColor.Black);
    }

    public ChessGameStatus GetStatus(ChessBoard board)
    {
        if (!board.IsEndGame)
        {
            return ChessGameStatus.Active;
        }

        var endGame = board.EndGame;
        if (endGame == null)
        {
            return ChessGameStatus.Draw;
        }

        if (endGame.EndgameType is EndgameType.Stalemate
            or EndgameType.DrawDeclared
            or EndgameType.InsufficientMaterial
            or EndgameType.Move50Rule
            or EndgameType.Repetition)
        {
            return ChessGameStatus.Draw;
        }

        return endGame.WonSide?.ToString() == "White"
            ? ChessGameStatus.WhiteWon
            : ChessGameStatus.BlackWon;
    }

    private static ChessMoveInfo? ResolveMove(
        ChessBoard board,
        IReadOnlyList<ChessMoveInfo> legalMoves,
        string normalized,
        out string errorMessage)
    {
        errorMessage = string.Empty;

        if (TryResolveSpecialMove(legalMoves, normalized, out var specialMove, out errorMessage))
        {
            return specialMove;
        }

        if (TryNormalizeCoordinateMove(normalized, out var uci))
        {
            var matches = legalMoves
                .Where(move => string.Equals(move.Uci, uci, StringComparison.OrdinalIgnoreCase))
                .ToList();

            return PickSingleMatch(matches, uci, out errorMessage);
        }

        if (TryResolveNaturalPawnMove(board, legalMoves, normalized, out var pawnMove, out errorMessage))
        {
            return pawnMove;
        }

        if (TryParseSan(board, normalized, legalMoves, out var sanMove, out errorMessage))
        {
            return sanMove;
        }

        var sanMatches = legalMoves
            .Where(move => string.Equals(move.San, normalized, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (sanMatches.Count > 0)
        {
            return PickSingleMatch(sanMatches, normalized, out errorMessage);
        }

        errorMessage = "합법수 목록에서 해당 수를 찾지 못했습니다. 좌표 표기(e2e4)나 SAN 표기(Nf3)를 사용해 주세요.";
        return null;
    }

    private static bool TryResolveSpecialMove(
        IReadOnlyList<ChessMoveInfo> legalMoves,
        string normalized,
        out ChessMoveInfo? move,
        out string errorMessage)
    {
        move = null;
        errorMessage = string.Empty;

        if (normalized.Contains("앙파상", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("en passant", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("e.p.", StringComparison.OrdinalIgnoreCase))
        {
            return TryPickSpecial(legalMoves.Where(m => m.IsEnPassant).ToList(), "앙파상", out move, out errorMessage);
        }

        if (normalized.Contains("캐슬", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("castle", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("castling", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("o-o", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("o-o-o", StringComparison.OrdinalIgnoreCase))
        {
            var castleMoves = legalMoves.Where(m => m.IsCastle).ToList();
            if (normalized.Contains("퀸", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("queen", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("o-o-o", StringComparison.OrdinalIgnoreCase))
            {
                castleMoves = castleMoves.Where(m => m.To[0] == 'c').ToList();
            }
            else if (normalized.Contains("킹", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("king", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("o-o", StringComparison.OrdinalIgnoreCase))
            {
                castleMoves = castleMoves.Where(m => m.To[0] == 'g').ToList();
            }

            return TryPickSpecial(castleMoves, "캐슬링", out move, out errorMessage);
        }

        return false;
    }

    private static bool TryPickSpecial(
        IReadOnlyList<ChessMoveInfo> matches,
        string moveName,
        out ChessMoveInfo? move,
        out string errorMessage)
    {
        move = null;
        errorMessage = string.Empty;

        if (matches.Count == 1)
        {
            move = matches[0];
            return true;
        }

        errorMessage = matches.Count == 0
            ? $"현재 {moveName}이 가능한 수가 없습니다."
            : $"{moveName} 가능한 수가 여러 개입니다. 어느 쪽인지 더 구체적으로 말해 주세요.";
        return true;
    }

    private static ChessMoveInfo? PickSingleMatch(
        IReadOnlyList<ChessMoveInfo> matches,
        string requestedMove,
        out string errorMessage)
    {
        errorMessage = string.Empty;
        if (matches.Count == 1)
        {
            return matches[0];
        }

        errorMessage = matches.Count == 0
            ? $"'{requestedMove}'는 현재 합법수가 아닙니다."
            : $"'{requestedMove}'로 해석되는 합법수가 여러 개입니다. 더 구체적으로 적어 주세요.";
        return null;
    }

    private static bool TryParseSan(
        ChessBoard board,
        string normalized,
        IReadOnlyList<ChessMoveInfo> legalMoves,
        out ChessMoveInfo? move,
        out string errorMessage)
    {
        move = null;
        errorMessage = string.Empty;

        try
        {
            if (!board.TryParseFromSan(normalized, out var parsedMove, true))
            {
                return false;
            }

            var uci = BuildUci(parsedMove);
            move = legalMoves.FirstOrDefault(m => string.Equals(m.Uci, uci, StringComparison.OrdinalIgnoreCase));
            if (move != null)
            {
                return true;
            }

            errorMessage = $"'{normalized}'는 현재 합법수가 아닙니다.";
            return true;
        }
        catch (ChessException e)
        {
            errorMessage = e.Message;
            return true;
        }
    }

    private static bool TryResolveNaturalPawnMove(
        ChessBoard board,
        IReadOnlyList<ChessMoveInfo> legalMoves,
        string normalized,
        out ChessMoveInfo? move,
        out string errorMessage)
    {
        move = null;
        errorMessage = string.Empty;

        if (!normalized.Contains("폰", StringComparison.OrdinalIgnoreCase)
            && !normalized.Contains("pawn", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var ordinal = ResolveOrdinal(normalized);
        if (ordinal == null)
        {
            return false;
        }

        int? rankDelta = null;
        if (normalized.Contains("두 칸", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("두칸", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("2칸", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("two", StringComparison.OrdinalIgnoreCase))
        {
            rankDelta = 2;
        }
        else if (normalized.Contains("한 칸", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("한칸", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("1칸", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("one", StringComparison.OrdinalIgnoreCase))
        {
            rankDelta = 1;
        }

        var pawnMoves = legalMoves
            .Where(moveInfo => moveInfo.Piece.EndsWith('p')
                && (!rankDelta.HasValue || Math.Abs(moveInfo.To[1] - moveInfo.From[1]) == rankDelta.Value))
            .ToList();
        if (pawnMoves.Count == 0)
        {
            errorMessage = "조건에 맞는 폰 이동이 현재 합법수에 없습니다.";
            return true;
        }

        var pawnFiles = pawnMoves
            .Select(m => m.From)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(square => square[0])
            .ToList();

        var index = ordinal.Value - 1;
        if (index < 0 || index >= pawnFiles.Count)
        {
            errorMessage = $"현재 조건에 맞는 폰이 {pawnFiles.Count}개뿐입니다.";
            return true;
        }

        var from = pawnFiles[index];
        var matches = pawnMoves.Where(m => m.From == from).ToList();
        move = PickSingleMatch(matches, normalized, out errorMessage);
        return true;
    }

    private static int? ResolveOrdinal(string text)
    {
        if (text.Contains("첫", StringComparison.OrdinalIgnoreCase) || text.Contains("1")) return 1;
        if (text.Contains("두", StringComparison.OrdinalIgnoreCase) || text.Contains("둘", StringComparison.OrdinalIgnoreCase) || text.Contains("2")) return 2;
        if (text.Contains("세", StringComparison.OrdinalIgnoreCase) || text.Contains("셋", StringComparison.OrdinalIgnoreCase) || text.Contains("3")) return 3;
        if (text.Contains("네", StringComparison.OrdinalIgnoreCase) || text.Contains("넷", StringComparison.OrdinalIgnoreCase) || text.Contains("4")) return 4;
        if (text.Contains("다섯", StringComparison.OrdinalIgnoreCase) || text.Contains("5")) return 5;
        if (text.Contains("여섯", StringComparison.OrdinalIgnoreCase) || text.Contains("6")) return 6;
        if (text.Contains("일곱", StringComparison.OrdinalIgnoreCase) || text.Contains("7")) return 7;
        if (text.Contains("여덟", StringComparison.OrdinalIgnoreCase) || text.Contains("8")) return 8;
        return null;
    }

    private static bool TryNormalizeCoordinateMove(string text, out string uci)
    {
        uci = string.Empty;
        var normalized = text
            .Replace(" ", string.Empty)
            .Replace("->", string.Empty)
            .Replace("=>", string.Empty)
            .Replace("-", string.Empty)
            .ToLowerInvariant();

        var match = UciMoveRegex().Match(normalized);
        if (!match.Success)
        {
            return false;
        }

        uci = match.Value;
        return true;
    }

    private static string NormalizeMoveText(string moveText)
    {
        return (moveText ?? string.Empty)
            .Trim()
            .Replace('０', '0')
            .Replace('１', '1')
            .Replace('２', '2')
            .Replace('３', '3')
            .Replace('４', '4')
            .Replace('５', '5')
            .Replace('６', '6')
            .Replace('７', '7')
            .Replace('８', '8')
            .Replace('９', '9');
    }

    private static ChessMoveInfo BuildMoveInfo(Move move)
    {
        var promotion = ResolvePromotion(move);
        return new ChessMoveInfo(
            BuildUci(move),
            move.OriginalPosition.ToString(),
            move.NewPosition.ToString(),
            move.Piece?.ToString() ?? string.Empty,
            string.IsNullOrWhiteSpace(move.San) ? null : move.San,
            move.CapturedPiece != null,
            move.IsCheck,
            move.IsMate,
            move.Parameter is MoveCastle,
            move.Parameter is MoveEnPassant,
            promotion,
            move);
    }

    private static string BuildUci(Move move)
    {
        return move.OriginalPosition.ToString()
            + move.NewPosition
            + ResolvePromotion(move);
    }

    private static string? ResolvePromotion(Move move)
    {
        if (move.Parameter is not MovePromotion promotion)
        {
            return null;
        }

        var shortStr = promotion.ShortStr;
        if (string.IsNullOrWhiteSpace(shortStr))
        {
            return "q";
        }

        return shortStr[^1].ToString().ToLowerInvariant();
    }

    [GeneratedRegex(@"^[a-h][1-8][a-h][1-8][qrbn]?$", RegexOptions.IgnoreCase)]
    private static partial Regex UciMoveRegex();
}
