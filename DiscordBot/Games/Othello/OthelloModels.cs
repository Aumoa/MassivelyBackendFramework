namespace DiscordBot.Games.Othello;

internal enum OthelloSide
{
    Black,
    White
}

internal enum OthelloDisc
{
    Empty,
    Black,
    White
}

internal enum OthelloGameStatus
{
    Active,
    BlackWon,
    WhiteWon,
    Draw
}

internal sealed record OthelloParticipant(
    string DisplayName,
    string? UserId,
    bool IsAi)
{
    public static OthelloParticipant Human(string displayName, string userId) => new(displayName, userId, false);

    public static OthelloParticipant Ai(string displayName = "AI") => new(displayName, null, true);
}

internal sealed class OthelloBoard
{
    public OthelloDisc[,] Cells { get; } = new OthelloDisc[8, 8];

    public OthelloSide CurrentSide { get; set; } = OthelloSide.Black;

    public OthelloBoard Clone()
    {
        var clone = new OthelloBoard
        {
            CurrentSide = CurrentSide
        };

        for (var file = 0; file < 8; file++)
        {
            for (var rank = 0; rank < 8; rank++)
            {
                clone.Cells[file, rank] = Cells[file, rank];
            }
        }

        return clone;
    }
}

internal sealed record OthelloMoveInfo(
    string Coordinate,
    OthelloSide Side,
    IReadOnlyList<string> FlippedSquares,
    bool IsCorner,
    bool IsEdge)
{
    public int FlipCount => FlippedSquares.Count;
}

internal sealed record OthelloMoveRecord(
    int Number,
    OthelloSide Side,
    string Coordinate,
    int FlipCount,
    string ActorName,
    bool IsPass,
    DateTimeOffset PlayedAt);

internal sealed class OthelloGameSession
{
    public required string Id { get; init; }

    public string? GuildId { get; init; }

    public required string ChannelId { get; init; }

    public required OthelloParticipant Black { get; init; }

    public required OthelloParticipant White { get; init; }

    public required OthelloBoard Board { get; init; }

    public required string Difficulty { get; init; }

    public required string AiPersonality { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public OthelloGameStatus Status { get; set; } = OthelloGameStatus.Active;

    public List<OthelloMoveRecord> MoveHistory { get; } = [];

    public SemaphoreSlim SyncRoot { get; } = new(1);

    public bool IsActive => Status == OthelloGameStatus.Active;

    public OthelloParticipant CurrentParticipant => CurrentSide == OthelloSide.Black ? Black : White;

    public OthelloSide CurrentSide => Board.CurrentSide;

    public OthelloSide? SideOfUser(string userId)
    {
        if (Black.UserId == userId)
        {
            return OthelloSide.Black;
        }

        if (White.UserId == userId)
        {
            return OthelloSide.White;
        }

        return null;
    }

    public OthelloParticipant ParticipantOf(OthelloSide side) => side == OthelloSide.Black ? Black : White;

    public OthelloParticipant OpponentOf(OthelloSide side) => side == OthelloSide.Black ? White : Black;
}

internal sealed record OthelloGameActionResult(
    bool Success,
    string Message,
    OthelloGameSession? Session,
    byte[]? BoardImage,
    string? FileName)
{
    public bool IsGameOver { get; init; }

    public string? SystemNoticeTitle { get; init; }

    public string? SystemNotice { get; init; }
}
