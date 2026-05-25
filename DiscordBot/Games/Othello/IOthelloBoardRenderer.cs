namespace DiscordBot.Games.Othello;

internal interface IOthelloBoardRenderer
{
    ValueTask<byte[]> RenderAsync(
        OthelloGameSession session,
        CancellationToken cancellationToken = default);
}
