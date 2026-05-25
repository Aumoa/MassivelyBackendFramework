using Chess;

namespace DiscordBot.Games.Chess;

internal interface IChessEngine
{
    ChessBoard CreateBoard();

    IReadOnlyList<ChessMoveInfo> GetLegalMoves(ChessBoard board);

    bool TryApplyMove(ChessBoard board, string moveText, out ChessMoveInfo? appliedMove, out string errorMessage);

    void Resign(ChessBoard board, ChessSide side);

    ChessGameStatus GetStatus(ChessBoard board);
}
