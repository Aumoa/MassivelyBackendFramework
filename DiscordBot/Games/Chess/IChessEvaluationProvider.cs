namespace DiscordBot.Games.Chess;

internal interface IChessEvaluationProvider
{
    ValueTask<IReadOnlyDictionary<string, ChessMoveEvaluation>> EvaluateAsync(
        ChessGameSession session,
        IReadOnlyList<ChessMoveInfo> legalMoves,
        CancellationToken cancellationToken = default);
}

internal sealed record ChessMoveEvaluation(
    int? CentipawnScore,
    string Label,
    string? Note);

internal sealed class NullChessEvaluationProvider : IChessEvaluationProvider
{
    public ValueTask<IReadOnlyDictionary<string, ChessMoveEvaluation>> EvaluateAsync(
        ChessGameSession session,
        IReadOnlyList<ChessMoveInfo> legalMoves,
        CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult<IReadOnlyDictionary<string, ChessMoveEvaluation>>(
            new Dictionary<string, ChessMoveEvaluation>(StringComparer.OrdinalIgnoreCase));
    }
}
