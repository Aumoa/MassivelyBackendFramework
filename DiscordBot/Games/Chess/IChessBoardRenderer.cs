namespace DiscordBot.Games.Chess;

internal interface IChessBoardRenderer
{
    ValueTask<byte[]> RenderAsync(
        ChessGameSession session,
        ChessSide perspective,
        CancellationToken cancellationToken = default);
}
