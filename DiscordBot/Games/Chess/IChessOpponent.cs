namespace DiscordBot.Games.Chess;

internal interface IChessOpponent
{
    ValueTask<ChessMoveInfo?> ChooseMoveAsync(
        ChessGameSession session,
        IReadOnlyList<ChessMoveInfo> legalMoves,
        CancellationToken cancellationToken = default);
}
