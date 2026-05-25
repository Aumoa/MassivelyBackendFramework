using Chess;

namespace DiscordBot.Games.Chess;

internal enum ChessSide
{
    White,
    Black
}

internal enum ChessGameStatus
{
    Active,
    WhiteWon,
    BlackWon,
    Draw
}

internal sealed record ChessParticipant(
    string DisplayName,
    string? UserId,
    bool IsAi)
{
    public static ChessParticipant Human(string displayName, string userId) => new(displayName, userId, false);

    public static ChessParticipant Ai(string displayName = "AI") => new(displayName, null, true);
}

internal sealed record ChessMoveInfo(
    string Uci,
    string From,
    string To,
    string Piece,
    string? San,
    bool IsCapture,
    bool IsCheck,
    bool IsMate,
    bool IsCastle,
    bool IsEnPassant,
    string? Promotion,
    Move SourceMove);

internal sealed record ChessMoveRecord(
    int Number,
    ChessSide Side,
    string Uci,
    string? San,
    string ActorName,
    DateTimeOffset PlayedAt);

internal sealed class ChessGameSession
{
    public required string Id { get; init; }

    public string? GuildId { get; init; }

    public required string ChannelId { get; init; }

    public required ChessParticipant White { get; init; }

    public required ChessParticipant Black { get; init; }

    public required ChessBoard Board { get; init; }

    public required string Difficulty { get; init; }

    public required string AiPersonality { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ChessGameStatus Status { get; set; } = ChessGameStatus.Active;

    public List<ChessMoveRecord> MoveHistory { get; } = [];

    public SemaphoreSlim SyncRoot { get; } = new(1);

    public bool IsActive => Status == ChessGameStatus.Active && !Board.IsEndGame;

    public ChessParticipant CurrentParticipant => CurrentSide == ChessSide.White ? White : Black;

    public ChessSide CurrentSide => Board.Turn.ToString() == "White" ? ChessSide.White : ChessSide.Black;

    public ChessSide? SideOfUser(string userId)
    {
        if (White.UserId == userId)
        {
            return ChessSide.White;
        }

        if (Black.UserId == userId)
        {
            return ChessSide.Black;
        }

        return null;
    }

    public ChessParticipant ParticipantOf(ChessSide side) => side == ChessSide.White ? White : Black;

    public ChessParticipant OpponentOf(ChessSide side) => side == ChessSide.White ? Black : White;
}

internal sealed record ChessGameActionResult(
    bool Success,
    string Message,
    ChessGameSession? Session,
    byte[]? BoardImage,
    string? FileName)
{
    public bool IsGameOver { get; init; }

    public string? SystemNoticeTitle { get; init; }

    public string? SystemNotice { get; init; }
}
