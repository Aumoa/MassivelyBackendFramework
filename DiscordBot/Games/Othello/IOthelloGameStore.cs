namespace DiscordBot.Games.Othello;

internal interface IOthelloGameStore
{
    OthelloGameSession? FindActiveByUser(string userId);

    ValueTask AddAsync(OthelloGameSession session, CancellationToken cancellationToken = default);

    ValueTask RemoveAsync(OthelloGameSession session, CancellationToken cancellationToken = default);
}
