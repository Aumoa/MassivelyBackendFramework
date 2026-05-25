namespace DiscordBot.Games.Othello;

internal interface IOthelloGameService
{
    OthelloGameSession? FindActiveByUser(string userId);

    string BuildActiveGameInstruction(string userId, string channelId);

    ValueTask<OthelloGameActionResult> StartAsync(
        string? guildId,
        string channelId,
        string requestedByUserId,
        OthelloParticipant black,
        OthelloParticipant white,
        string difficulty,
        string aiPersonality,
        CancellationToken cancellationToken = default);

    ValueTask<OthelloGameActionResult> MoveAsync(
        string userId,
        string channelId,
        string moveText,
        CancellationToken cancellationToken = default);

    ValueTask<OthelloGameActionResult> PassAsync(
        string userId,
        string channelId,
        CancellationToken cancellationToken = default);

    ValueTask<OthelloGameActionResult> SurrenderAsync(
        string userId,
        string channelId,
        string reason,
        CancellationToken cancellationToken = default);

    ValueTask<OthelloGameActionResult> ShowAsync(
        string userId,
        string channelId,
        CancellationToken cancellationToken = default);
}
