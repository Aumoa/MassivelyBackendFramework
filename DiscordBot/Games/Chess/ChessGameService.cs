using System.Text;

namespace DiscordBot.Games.Chess;

internal sealed class ChessGameService(
    IChessGameStore store,
    IChessEngine engine,
    IChessOpponent opponent,
    IChessBoardRenderer renderer,
    ILogger<ChessGameService> logger) : IChessGameService
{
    public ChessGameSession? FindActiveByUser(string userId) => store.FindActiveByUser(userId);

    public string BuildActiveGameInstruction(string userId, string channelId)
    {
        var session = store.FindActiveByUser(userId);
        if (session == null)
        {
            return string.Empty;
        }

        if (session.ChannelId != channelId)
        {
            return $"사용자는 이미 다른 채널({session.ChannelId})에서 체스 게임을 진행 중입니다. 그 채널에서 계속하거나 먼저 게임을 종료해야 합니다.";
        }

        var side = session.SideOfUser(userId);
        var legalMoves = engine.GetLegalMoves(session.Board);
        var builder = new StringBuilder();
        builder.AppendLine("[활성 체스 게임]");
        builder.AppendLine("현재 사용자는 체스 게임 참여자입니다. 사용자의 메시지는 기본적으로 게임 진행 의도로 해석하세요.");
        builder.AppendLine("말 이동이면 move_chess를 호출하고, 그만두기/졌다/항복/끝내기 의도면 surrender_chess를 호출하세요.");
        builder.AppendLine("명백한 잡담이나 일반 질문이면 체스 게임 진행 중이라고 말하고 계속 둘지 종료할지 물어보세요.");
        builder.AppendLine($"사용자 색: {(side == ChessSide.White ? "백" : "흑")}");
        builder.AppendLine($"현재 턴: {(session.CurrentSide == ChessSide.White ? "백" : "흑")} ({session.CurrentParticipant.DisplayName})");
        builder.AppendLine("보드 이미지는 항상 백 기준입니다. 왼쪽 아래는 a1, 오른쪽 아래는 h1, 왼쪽 위는 a8입니다.");
        builder.AppendLine($"FEN: {session.Board.ToFen()}");
        builder.AppendLine();
        builder.AppendLine("[현재 합법수]");
        foreach (var move in legalMoves)
        {
            builder.Append("- ").Append(move.Uci);
            if (!string.IsNullOrWhiteSpace(move.San))
            {
                builder.Append(" / ").Append(move.San);
            }

            if (move.IsCastle) builder.Append(" / 캐슬링");
            if (move.IsEnPassant) builder.Append(" / 앙파상");
            if (move.Promotion != null) builder.Append(" / 승격=").Append(move.Promotion);
            if (move.IsCheck) builder.Append(" / 체크");
            if (move.IsMate) builder.Append(" / 메이트");
            builder.AppendLine();
        }

        return builder.ToString();
    }

    public async ValueTask<ChessGameActionResult> StartAsync(
        string? guildId,
        string channelId,
        string requestedByUserId,
        ChessParticipant white,
        ChessParticipant black,
        string difficulty,
        string aiPersonality,
        CancellationToken cancellationToken = default)
    {
        var participantValidation = ValidateParticipants(white, black);
        if (participantValidation != null)
        {
            return Failure(participantValidation);
        }

        var activeValidation = ValidateNoActiveGame(white, black);
        if (activeValidation != null)
        {
            return Failure(activeValidation);
        }

        var session = new ChessGameSession
        {
            Id = Guid.NewGuid().ToString("N"),
            GuildId = guildId,
            ChannelId = channelId,
            White = white,
            Black = black,
            Board = engine.CreateBoard(),
            Difficulty = NormalizeDifficulty(difficulty),
            AiPersonality = NormalizeAiPersonality(aiPersonality)
        };

        await store.AddAsync(session, cancellationToken);

        await session.SyncRoot.WaitAsync(cancellationToken);
        try
        {
            var message = new StringBuilder();
            message.AppendLine($"체스 게임을 시작했습니다. 백: {white.DisplayName}, 흑: {black.DisplayName}");
            message.AppendLine($"난이도: {session.Difficulty}, AI 성격: {session.AiPersonality}");

            if (session.CurrentParticipant.IsAi)
            {
                var aiMessage = await TryPlayAiTurnAsync(session, cancellationToken);
                message.AppendLine(aiMessage);
            }
            else
            {
                message.AppendLine($"현재 턴: {session.CurrentParticipant.DisplayName}");
            }

            var image = await RenderForUserAsync(session, requestedByUserId, cancellationToken);
            return Success(
                message.ToString().Trim(),
                session,
                image,
                "체스 게임 시작",
                "게임이 시작되었습니다. 참여자의 봇 멘션은 종료 전까지 체스 진행으로 우선 처리되며, 일반 대화 일부가 제한됩니다.\n게임을 종료하려면 AI에게 기권 또는 종료를 요청하세요.");
        }
        finally
        {
            session.SyncRoot.Release();
        }
    }

    public async ValueTask<ChessGameActionResult> MoveAsync(
        string userId,
        string channelId,
        string moveText,
        CancellationToken cancellationToken = default)
    {
        var session = store.FindActiveByUser(userId);
        if (session == null)
        {
            return Failure("진행 중인 체스 게임이 없습니다.");
        }

        if (session.ChannelId != channelId)
        {
            return Failure("진행 중인 체스 게임은 다른 채널에 있습니다. 게임을 시작한 채널에서 계속해 주세요.");
        }

        await session.SyncRoot.WaitAsync(cancellationToken);
        try
        {
            if (!session.IsActive)
            {
                await store.RemoveAsync(session, cancellationToken);
                return Failure("이미 종료된 체스 게임입니다.");
            }

            var side = session.SideOfUser(userId);
            if (side == null)
            {
                return Failure("이 체스 게임의 참여자가 아닙니다.");
            }

            if (session.CurrentParticipant.IsAi)
            {
                return Failure("아직 AI 차례입니다. 잠시 후 다시 시도해 주세요.");
            }

            if (session.CurrentParticipant.UserId != userId)
            {
                return Failure($"현재는 {session.CurrentParticipant.DisplayName} 차례입니다.");
            }

            var actorName = session.CurrentParticipant.DisplayName;
            if (!engine.TryApplyMove(session.Board, moveText, out var appliedMove, out var errorMessage) || appliedMove == null)
            {
                return Failure(errorMessage);
            }

            RecordMove(session, side.Value, appliedMove, actorName);
            UpdateStatus(session);

            var message = new StringBuilder();
            message.AppendLine($"{session.ParticipantOf(side.Value).DisplayName}: {FormatMove(appliedMove)}");

            if (session.IsActive && session.CurrentParticipant.IsAi)
            {
                var aiMessage = await TryPlayAiTurnAsync(session, cancellationToken);
                message.AppendLine(aiMessage);
            }

            if (session.IsActive)
            {
                message.AppendLine($"현재 턴: {session.CurrentParticipant.DisplayName}");
            }
            else
            {
                message.AppendLine(BuildEndSummary(session));
                await store.RemoveAsync(session, cancellationToken);
            }

            var image = await RenderForUserAsync(session, userId, cancellationToken);
            return Success(message.ToString().Trim(), session, image);
        }
        finally
        {
            session.SyncRoot.Release();
        }
    }

    public async ValueTask<ChessGameActionResult> SurrenderAsync(
        string userId,
        string channelId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var session = store.FindActiveByUser(userId);
        if (session == null)
        {
            return Failure("진행 중인 체스 게임이 없습니다.");
        }

        if (session.ChannelId != channelId)
        {
            return Failure("진행 중인 체스 게임은 다른 채널에 있습니다. 게임을 시작한 채널에서 종료해 주세요.");
        }

        await session.SyncRoot.WaitAsync(cancellationToken);
        try
        {
            var side = session.SideOfUser(userId);
            if (side == null)
            {
                return Failure("이 체스 게임의 참여자가 아닙니다.");
            }

            engine.Resign(session.Board, side.Value);
            UpdateStatus(session);
            await store.RemoveAsync(session, cancellationToken);

            var image = await RenderForUserAsync(session, userId, cancellationToken);
            var message = BuildEndSummary(session, $"{session.ParticipantOf(side.Value).DisplayName} 기권.");
            if (!string.IsNullOrWhiteSpace(reason))
            {
                message += $"\n사유: {reason.Trim()}";
            }

            return Success(message, session, image);
        }
        finally
        {
            session.SyncRoot.Release();
        }
    }

    public async ValueTask<ChessGameActionResult> ShowAsync(
        string userId,
        string channelId,
        CancellationToken cancellationToken = default)
    {
        var session = store.FindActiveByUser(userId);
        if (session == null)
        {
            return Failure("진행 중인 체스 게임이 없습니다.");
        }

        if (session.ChannelId != channelId)
        {
            return Failure("진행 중인 체스 게임은 다른 채널에 있습니다. 게임을 시작한 채널에서 확인해 주세요.");
        }

        await session.SyncRoot.WaitAsync(cancellationToken);
        try
        {
            var image = await RenderForUserAsync(session, userId, cancellationToken);
            return Success(BuildStatusMessage(session), session, image);
        }
        finally
        {
            session.SyncRoot.Release();
        }
    }

    private async ValueTask<string> TryPlayAiTurnAsync(
        ChessGameSession session,
        CancellationToken cancellationToken)
    {
        var aiSide = session.CurrentSide;
        var aiPlayer = session.CurrentParticipant;
        var legalMoves = engine.GetLegalMoves(session.Board);
        if (legalMoves.Count == 0)
        {
            UpdateStatus(session);
            return BuildEndSummary(session);
        }

        var move = await opponent.ChooseMoveAsync(session, legalMoves, cancellationToken);
        if (move == null)
        {
            UpdateStatus(session);
            return "AI가 둘 수 있는 합법수를 찾지 못했습니다.";
        }

        try
        {
            if (!session.Board.Move(move.SourceMove))
            {
                logger.LogWarning("AI selected an invalid chess move: {Move}", move.Uci);
                move = legalMoves[Random.Shared.Next(legalMoves.Count)];
                session.Board.Move(move.SourceMove);
            }
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "AI selected a chess move that failed at execution: {Move}", move.Uci);
            move = legalMoves[Random.Shared.Next(legalMoves.Count)];
            session.Board.Move(move.SourceMove);
        }

        RecordMove(session, aiSide, move, aiPlayer.DisplayName);
        UpdateStatus(session);
        return $"{aiPlayer.DisplayName}: {FormatMove(move)}";
    }

    private static string? ValidateParticipants(ChessParticipant white, ChessParticipant black)
    {
        if (white.IsAi && black.IsAi)
        {
            return "AI끼리만 두는 게임은 지원하지 않습니다.";
        }

        if (!white.IsAi
            && !black.IsAi
            && !string.IsNullOrWhiteSpace(white.UserId)
            && white.UserId == black.UserId)
        {
            return "같은 사용자를 백과 흑으로 동시에 지정할 수 없습니다.";
        }

        return null;
    }

    private string? ValidateNoActiveGame(ChessParticipant white, ChessParticipant black)
    {
        foreach (var participant in new[] { white, black })
        {
            if (participant.IsAi || string.IsNullOrWhiteSpace(participant.UserId))
            {
                continue;
            }

            if (store.FindActiveByUser(participant.UserId) != null)
            {
                return $"{participant.DisplayName}님은 이미 진행 중인 체스 게임이 있습니다.";
            }
        }

        return null;
    }

    private async ValueTask<byte[]> RenderForUserAsync(
        ChessGameSession session,
        string userId,
        CancellationToken cancellationToken)
    {
        return await renderer.RenderAsync(session, ChessSide.White, cancellationToken);
    }

    private void UpdateStatus(ChessGameSession session)
    {
        session.Status = engine.GetStatus(session.Board);
        session.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static void RecordMove(
        ChessGameSession session,
        ChessSide side,
        ChessMoveInfo move,
        string actorName)
    {
        session.MoveHistory.Add(new ChessMoveRecord(
            session.MoveHistory.Count + 1,
            side,
            move.Uci,
            move.San,
            actorName,
            DateTimeOffset.UtcNow));
        session.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static ChessGameActionResult Success(
        string message,
        ChessGameSession session,
        byte[] boardImage,
        string? systemNoticeTitle = null,
        string? systemNotice = null)
    {
        return new ChessGameActionResult(true, message, session, boardImage, $"chess-{session.Id}.png")
        {
            IsGameOver = !session.IsActive,
            SystemNoticeTitle = systemNoticeTitle ?? (!session.IsActive ? "체스 게임 종료" : null),
            SystemNotice = systemNotice ?? (!session.IsActive ? "게임이 종료되었습니다. 일반 대화를 다시 사용할 수 있습니다." : null)
        };
    }

    private static ChessGameActionResult Failure(string message)
    {
        return new ChessGameActionResult(false, message, null, null, null);
    }

    private static string BuildStatusMessage(ChessGameSession session)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"백: {session.White.DisplayName}");
        builder.AppendLine($"흑: {session.Black.DisplayName}");
        builder.AppendLine($"현재 턴: {session.CurrentParticipant.DisplayName}");
        builder.AppendLine($"FEN: {session.Board.ToFen()}");
        if (session.MoveHistory.Count > 0)
        {
            var last = session.MoveHistory[^1];
            builder.AppendLine($"마지막 수: {last.ActorName} {last.San ?? last.Uci}");
        }

        return builder.ToString().Trim();
    }

    private static string BuildEndMessage(ChessGameSession session)
    {
        return session.Status switch
        {
            ChessGameStatus.WhiteWon => $"게임 종료. 백({session.White.DisplayName}) 승리.",
            ChessGameStatus.BlackWon => $"게임 종료. 흑({session.Black.DisplayName}) 승리.",
            ChessGameStatus.Draw => "게임 종료. 무승부.",
            _ => "게임이 계속 진행 중입니다."
        };
    }

    private static string BuildEndSummary(ChessGameSession session, string? reason = null)
    {
        var builder = new StringBuilder();
        builder.AppendLine("[체스 게임 종료]");
        if (!string.IsNullOrWhiteSpace(reason))
        {
            builder.AppendLine(reason.Trim());
        }

        builder.AppendLine($"결과: {BuildEndMessage(session)}");
        builder.AppendLine($"백: {session.White.DisplayName}");
        builder.AppendLine($"흑: {session.Black.DisplayName}");
        builder.AppendLine($"총 수: {session.MoveHistory.Count} ply");
        if (session.MoveHistory.Count > 0)
        {
            var last = session.MoveHistory[^1];
            builder.AppendLine($"마지막 수: {last.ActorName} {last.San ?? last.Uci}");
        }

        builder.AppendLine($"마지막 FEN: {session.Board.ToFen()}");
        builder.AppendLine("현재 체스 세션은 종료되었습니다. 새 판은 다시 체스를 시작하자고 말하면 시작할 수 있습니다.");
        return builder.ToString().Trim();
    }

    private static string FormatMove(ChessMoveInfo move)
    {
        var notation = string.IsNullOrWhiteSpace(move.San) ? move.Uci : move.San;
        if (move.IsMate)
        {
            return $"{notation} 체크메이트";
        }

        if (move.IsCheck)
        {
            return $"{notation} 체크";
        }

        return notation;
    }

    private static string NormalizeDifficulty(string difficulty)
    {
        var normalized = difficulty.Trim().ToLowerInvariant();
        return string.IsNullOrWhiteSpace(normalized) ? "normal" : normalized;
    }

    private static string NormalizeAiPersonality(string aiPersonality)
    {
        var normalized = aiPersonality.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? "balanced, human-like" : normalized;
    }
}
