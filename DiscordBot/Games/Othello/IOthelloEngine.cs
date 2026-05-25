namespace DiscordBot.Games.Othello;

internal interface IOthelloEngine
{
    OthelloBoard CreateBoard();

    IReadOnlyList<OthelloMoveInfo> GetLegalMoves(OthelloBoard board);

    IReadOnlyList<OthelloMoveInfo> GetLegalMoves(OthelloBoard board, OthelloSide side);

    bool TryApplyMove(OthelloBoard board, string moveText, out OthelloMoveInfo? appliedMove, out string errorMessage);

    bool CanPass(OthelloBoard board);

    bool Pass(OthelloBoard board, out string errorMessage);

    OthelloGameStatus GetStatus(OthelloBoard board);

    (int Black, int White) CountDiscs(OthelloBoard board);

    string ToPositionString(OthelloBoard board);
}
