namespace DiscordBot.Games.Chess;

internal interface IChessGameStore
{
    ChessGameSession? FindActiveByUser(string userId);

    ValueTask AddAsync(ChessGameSession session, CancellationToken cancellationToken = default);

    ValueTask RemoveAsync(ChessGameSession session, CancellationToken cancellationToken = default);
}
