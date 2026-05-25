using System.Text;
using System.Text.RegularExpressions;

namespace DiscordBot.Games.Othello;

internal sealed partial class OthelloEngine : IOthelloEngine
{
    private static readonly (int File, int Rank)[] Directions =
    [
        (-1, -1), (0, -1), (1, -1),
        (-1, 0),           (1, 0),
        (-1, 1),  (0, 1),  (1, 1)
    ];

    public OthelloBoard CreateBoard()
    {
        var board = new OthelloBoard();
        board.Cells[3, 3] = OthelloDisc.White; // d4
        board.Cells[4, 4] = OthelloDisc.White; // e5
        board.Cells[4, 3] = OthelloDisc.Black; // e4
        board.Cells[3, 4] = OthelloDisc.Black; // d5
        return board;
    }

    public IReadOnlyList<OthelloMoveInfo> GetLegalMoves(OthelloBoard board)
    {
        return GetLegalMoves(board, board.CurrentSide);
    }

    public IReadOnlyList<OthelloMoveInfo> GetLegalMoves(OthelloBoard board, OthelloSide side)
    {
        List<OthelloMoveInfo> moves = [];
        for (var file = 0; file < 8; file++)
        {
            for (var rank = 0; rank < 8; rank++)
            {
                if (board.Cells[file, rank] != OthelloDisc.Empty)
                {
                    continue;
                }

                var flippedSquares = GetFlippedSquares(board, side, file, rank);
                if (flippedSquares.Count == 0)
                {
                    continue;
                }

                var coordinate = ToCoordinate(file, rank);
                moves.Add(new OthelloMoveInfo(
                    coordinate,
                    side,
                    flippedSquares,
                    IsCorner(file, rank),
                    IsEdge(file, rank)));
            }
        }

        return moves
            .OrderBy(move => move.Coordinate[0])
            .ThenBy(move => move.Coordinate[1])
            .ToList();
    }

    public bool TryApplyMove(OthelloBoard board, string moveText, out OthelloMoveInfo? appliedMove, out string errorMessage)
    {
        appliedMove = null;
        errorMessage = string.Empty;

        var legalMoves = GetLegalMoves(board);
        if (legalMoves.Count == 0)
        {
            errorMessage = "현재 둘 수 있는 곳이 없습니다. 패스해야 합니다.";
            return false;
        }

        var move = ResolveMove(legalMoves, moveText, out errorMessage);
        if (move == null)
        {
            return false;
        }

        var (file, rank) = FromCoordinate(move.Coordinate);
        board.Cells[file, rank] = ToDisc(board.CurrentSide);
        foreach (var square in move.FlippedSquares)
        {
            var (flippedFile, flippedRank) = FromCoordinate(square);
            board.Cells[flippedFile, flippedRank] = ToDisc(board.CurrentSide);
        }

        board.CurrentSide = Opponent(board.CurrentSide);
        appliedMove = move;
        return true;
    }

    public bool CanPass(OthelloBoard board)
    {
        return GetLegalMoves(board).Count == 0 && GetLegalMoves(board, Opponent(board.CurrentSide)).Count > 0;
    }

    public bool Pass(OthelloBoard board, out string errorMessage)
    {
        errorMessage = string.Empty;
        if (!CanPass(board))
        {
            errorMessage = GetLegalMoves(board).Count > 0
                ? "현재는 둘 수 있는 곳이 있어서 패스할 수 없습니다."
                : "양쪽 모두 둘 수 있는 곳이 없어 게임이 종료되었습니다.";
            return false;
        }

        board.CurrentSide = Opponent(board.CurrentSide);
        return true;
    }

    public OthelloGameStatus GetStatus(OthelloBoard board)
    {
        if (GetLegalMoves(board, OthelloSide.Black).Count > 0
            || GetLegalMoves(board, OthelloSide.White).Count > 0)
        {
            return OthelloGameStatus.Active;
        }

        var (black, white) = CountDiscs(board);
        if (black > white)
        {
            return OthelloGameStatus.BlackWon;
        }

        if (white > black)
        {
            return OthelloGameStatus.WhiteWon;
        }

        return OthelloGameStatus.Draw;
    }

    public (int Black, int White) CountDiscs(OthelloBoard board)
    {
        var black = 0;
        var white = 0;
        for (var file = 0; file < 8; file++)
        {
            for (var rank = 0; rank < 8; rank++)
            {
                if (board.Cells[file, rank] == OthelloDisc.Black)
                {
                    black++;
                }
                else if (board.Cells[file, rank] == OthelloDisc.White)
                {
                    white++;
                }
            }
        }

        return (black, white);
    }

    public string ToPositionString(OthelloBoard board)
    {
        var builder = new StringBuilder();
        builder.AppendLine("  a b c d e f g h");
        for (var rank = 7; rank >= 0; rank--)
        {
            builder.Append(rank + 1).Append(' ');
            for (var file = 0; file < 8; file++)
            {
                var c = board.Cells[file, rank] switch
                {
                    OthelloDisc.Black => 'B',
                    OthelloDisc.White => 'W',
                    _ => '.'
                };
                builder.Append(c);
                if (file < 7)
                {
                    builder.Append(' ');
                }
            }

            builder.Append(' ').Append(rank + 1).AppendLine();
        }

        builder.AppendLine("  a b c d e f g h");
        return builder.ToString().Trim();
    }

    private static OthelloMoveInfo? ResolveMove(
        IReadOnlyList<OthelloMoveInfo> legalMoves,
        string moveText,
        out string errorMessage)
    {
        errorMessage = string.Empty;
        var normalized = NormalizeMoveText(moveText);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            errorMessage = "둘 위치를 알아듣지 못했습니다.";
            return null;
        }

        if (TryExtractCoordinate(normalized, out var coordinate))
        {
            return PickSingleMatch(
                legalMoves.Where(move => string.Equals(move.Coordinate, coordinate, StringComparison.OrdinalIgnoreCase)).ToList(),
                coordinate,
                out errorMessage);
        }

        var directionalCoordinate = ResolveDirectionalCoordinate(normalized);
        if (directionalCoordinate != null)
        {
            return PickSingleMatch(
                legalMoves.Where(move => string.Equals(move.Coordinate, directionalCoordinate, StringComparison.OrdinalIgnoreCase)).ToList(),
                directionalCoordinate,
                out errorMessage);
        }

        var candidates = legalMoves.ToList();
        if (ContainsAny(normalized, "구석", "corner"))
        {
            candidates = candidates.Where(move => move.IsCorner).ToList();
        }

        if (ContainsAny(normalized, "가장 많이", "제일 많이", "많이 뒤집", "최대", "maximum", "most"))
        {
            var max = candidates.Count == 0 ? 0 : candidates.Max(move => move.FlipCount);
            candidates = candidates.Where(move => move.FlipCount == max).ToList();
        }
        else if (ContainsAny(normalized, "가장 적게", "제일 적게", "적게 뒤집", "최소", "minimum", "least"))
        {
            var min = candidates.Count == 0 ? 0 : candidates.Min(move => move.FlipCount);
            candidates = candidates.Where(move => move.FlipCount == min).ToList();
        }
        else if (ContainsAny(normalized, "안전", "safe"))
        {
            candidates = candidates
                .OrderByDescending(move => move.IsCorner)
                .ThenByDescending(move => move.IsEdge)
                .ThenByDescending(move => move.FlipCount)
                .ToList();
        }

        if (ContainsAny(normalized, "왼쪽", "left"))
        {
            candidates = candidates.OrderBy(move => move.Coordinate[0]).ToList();
        }
        else if (ContainsAny(normalized, "오른쪽", "right"))
        {
            candidates = candidates.OrderByDescending(move => move.Coordinate[0]).ToList();
        }

        if (ContainsAny(normalized, "위", "상단", "top", "up"))
        {
            candidates = candidates.OrderByDescending(move => move.Coordinate[1]).ToList();
        }
        else if (ContainsAny(normalized, "아래", "하단", "bottom", "down"))
        {
            candidates = candidates.OrderBy(move => move.Coordinate[1]).ToList();
        }

        if (!ReferenceEquals(candidates, legalMoves) && candidates.Count == 1)
        {
            return candidates[0];
        }

        errorMessage = candidates.Count == 0
            ? "조건에 맞는 합법수가 없습니다."
            : "해석 가능한 위치가 여러 곳입니다. 좌표(a1-h8)로 더 구체적으로 말해 주세요.";
        return null;
    }

    private static OthelloMoveInfo? PickSingleMatch(
        IReadOnlyList<OthelloMoveInfo> matches,
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
            : $"'{requestedMove}'로 해석되는 합법수가 여러 개입니다. 좌표를 더 구체적으로 말해 주세요.";
        return null;
    }

    private static string? ResolveDirectionalCoordinate(string normalized)
    {
        var left = ContainsAny(normalized, "왼쪽", "left");
        var right = ContainsAny(normalized, "오른쪽", "right");
        var up = ContainsAny(normalized, "위", "상단", "top", "up");
        var down = ContainsAny(normalized, "아래", "하단", "bottom", "down");

        if (left && up) return "a8";
        if (right && up) return "h8";
        if (left && down) return "a1";
        if (right && down) return "h1";
        return null;
    }

    private static IReadOnlyList<string> GetFlippedSquares(OthelloBoard board, OthelloSide side, int file, int rank)
    {
        List<string> flipped = [];
        var ownDisc = ToDisc(side);
        var opponentDisc = ToDisc(Opponent(side));

        foreach (var (fileDelta, rankDelta) in Directions)
        {
            List<string> line = [];
            var currentFile = file + fileDelta;
            var currentRank = rank + rankDelta;

            while (IsInside(currentFile, currentRank)
                && board.Cells[currentFile, currentRank] == opponentDisc)
            {
                line.Add(ToCoordinate(currentFile, currentRank));
                currentFile += fileDelta;
                currentRank += rankDelta;
            }

            if (line.Count > 0
                && IsInside(currentFile, currentRank)
                && board.Cells[currentFile, currentRank] == ownDisc)
            {
                flipped.AddRange(line);
            }
        }

        return flipped;
    }

    private static bool TryExtractCoordinate(string text, out string coordinate)
    {
        coordinate = string.Empty;
        var match = CoordinateRegex().Match(text.Replace(" ", string.Empty));
        if (!match.Success)
        {
            return false;
        }

        coordinate = match.Value.ToLowerInvariant();
        return true;
    }

    private static string NormalizeMoveText(string moveText)
    {
        return (moveText ?? string.Empty)
            .Trim()
            .Replace('Ａ', 'A')
            .Replace('Ｂ', 'B')
            .Replace('Ｃ', 'C')
            .Replace('Ｄ', 'D')
            .Replace('Ｅ', 'E')
            .Replace('Ｆ', 'F')
            .Replace('Ｇ', 'G')
            .Replace('Ｈ', 'H')
            .Replace('ａ', 'a')
            .Replace('ｂ', 'b')
            .Replace('ｃ', 'c')
            .Replace('ｄ', 'd')
            .Replace('ｅ', 'e')
            .Replace('ｆ', 'f')
            .Replace('ｇ', 'g')
            .Replace('ｈ', 'h')
            .Replace('１', '1')
            .Replace('２', '2')
            .Replace('３', '3')
            .Replace('４', '4')
            .Replace('５', '5')
            .Replace('６', '6')
            .Replace('７', '7')
            .Replace('８', '8')
            .ToLowerInvariant();
    }

    private static bool ContainsAny(string value, params string[] needles)
    {
        return needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsInside(int file, int rank) => file is >= 0 and < 8 && rank is >= 0 and < 8;

    private static bool IsCorner(int file, int rank) => (file is 0 or 7) && (rank is 0 or 7);

    private static bool IsEdge(int file, int rank) => file is 0 or 7 || rank is 0 or 7;

    private static OthelloDisc ToDisc(OthelloSide side) => side == OthelloSide.Black ? OthelloDisc.Black : OthelloDisc.White;

    private static OthelloSide Opponent(OthelloSide side) => side == OthelloSide.Black ? OthelloSide.White : OthelloSide.Black;

    private static string ToCoordinate(int file, int rank) => $"{(char)('a' + file)}{rank + 1}";

    private static (int File, int Rank) FromCoordinate(string coordinate) => (coordinate[0] - 'a', coordinate[1] - '1');

    [GeneratedRegex(@"[a-h][1-8]", RegexOptions.IgnoreCase)]
    private static partial Regex CoordinateRegex();
}
