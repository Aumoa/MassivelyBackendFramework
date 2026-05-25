namespace DiscordBot.Games.Othello;

internal interface IOthelloOpponent
{
    ValueTask<OthelloMoveInfo?> ChooseMoveAsync(
        OthelloGameSession session,
        IReadOnlyList<OthelloMoveInfo> legalMoves,
        CancellationToken cancellationToken = default);
}
