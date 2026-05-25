namespace DiscordBot.Games.Chess;

internal interface IChessGameService
{
    ChessGameSession? FindActiveByUser(string userId);

    string BuildActiveGameInstruction(string userId, string channelId);

    ValueTask<ChessGameActionResult> StartAsync(
        string? guildId,
        string channelId,
        string requestedByUserId,
        ChessParticipant white,
        ChessParticipant black,
        string difficulty,
        string aiPersonality,
        CancellationToken cancellationToken = default);

    ValueTask<ChessGameActionResult> MoveAsync(
        string userId,
        string channelId,
        string moveText,
        CancellationToken cancellationToken = default);

    ValueTask<ChessGameActionResult> SurrenderAsync(
        string userId,
        string channelId,
        string reason,
        CancellationToken cancellationToken = default);

    ValueTask<ChessGameActionResult> ShowAsync(
        string userId,
        string channelId,
        CancellationToken cancellationToken = default);
}
