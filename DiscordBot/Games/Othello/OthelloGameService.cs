using System.Text;

namespace DiscordBot.Games.Othello;

internal sealed class OthelloGameService(
    IOthelloGameStore store,
    IOthelloEngine engine,
    IOthelloOpponent opponent,
    IOthelloBoardRenderer renderer) : IOthelloGameService
{
    public OthelloGameSession? FindActiveByUser(string userId) => store.FindActiveByUser(userId);

    public string BuildActiveGameInstruction(string userId, string channelId)
    {
        var session = store.FindActiveByUser(userId);
        if (session == null)
        {
            return string.Empty;
        }

        if (session.ChannelId != channelId)
        {
            return $"사용자는 이미 다른 채널({session.ChannelId})에서 오셀로 게임을 진행 중입니다. 그 채널에서 계속하거나 먼저 게임을 종료해야 합니다.";
        }

        var side = session.SideOfUser(userId);
        var legalMoves = engine.GetLegalMoves(session.Board);
        var (blackCount, whiteCount) = engine.CountDiscs(session.Board);
        var builder = new StringBuilder();
        builder.AppendLine("[활성 오셀로 게임]");
        builder.AppendLine("현재 사용자는 오셀로 게임 참여자입니다. 사용자의 메시지는 기본적으로 게임 진행 의도로 해석하세요.");
        builder.AppendLine("돌을 둘 위치를 말하면 move_othello를 호출하고, 패스 의도면 pass_othello를 호출하세요.");
        builder.AppendLine("그만두기/졌다/항복/끝내기 의도면 surrender_othello를 호출하세요.");
        builder.AppendLine("명백한 잡담이나 일반 질문이면 오셀로 게임 진행 중이라고 말하고 계속 둘지 종료할지 물어보세요.");
        builder.AppendLine($"사용자 색: {SideName(side ?? session.CurrentSide)}");
        builder.AppendLine($"현재 턴: {SideName(session.CurrentSide)} ({session.CurrentParticipant.DisplayName})");
        builder.AppendLine($"점수: 흑 {blackCount}, 백 {whiteCount}");
        builder.AppendLine("보드 이미지는 항상 백 기준 좌표입니다. 왼쪽 아래는 a1, 오른쪽 아래는 h1, 왼쪽 위는 a8입니다.");
        builder.AppendLine();
        builder.AppendLine("[현재 보드]");
        builder.AppendLine(engine.ToPositionString(session.Board));
        builder.AppendLine();
        builder.AppendLine("[현재 합법수]");
        if (legalMoves.Count == 0)
        {
            builder.AppendLine("- 합법수가 없습니다. 패스해야 합니다.");
        }
        else
        {
            foreach (var move in legalMoves)
            {
                builder.Append("- ").Append(move.Coordinate)
                    .Append(" / 뒤집는 돌 ").Append(move.FlipCount).Append("개");
                if (move.IsCorner) builder.Append(" / 구석");
                if (move.IsEdge) builder.Append(" / 변");
                builder.AppendLine();
            }
        }

        return builder.ToString();
    }

    public async ValueTask<OthelloGameActionResult> StartAsync(
        string? guildId,
        string channelId,
        string requestedByUserId,
        OthelloParticipant black,
        OthelloParticipant white,
        string difficulty,
        string aiPersonality,
        CancellationToken cancellationToken = default)
    {
        var participantValidation = ValidateParticipants(black, white);
        if (participantValidation != null)
        {
            return Failure(participantValidation);
        }

        var activeValidation = ValidateNoActiveGame(black, white);
        if (activeValidation != null)
        {
            return Failure(activeValidation);
        }

        var session = new OthelloGameSession
        {
            Id = Guid.NewGuid().ToString("N"),
            GuildId = guildId,
            ChannelId = channelId,
            Black = black,
            White = white,
            Board = engine.CreateBoard(),
            Difficulty = NormalizeDifficulty(difficulty),
            AiPersonality = NormalizeAiPersonality(aiPersonality)
        };

        await store.AddAsync(session, cancellationToken);

        await session.SyncRoot.WaitAsync(cancellationToken);
        try
        {
            var message = new StringBuilder();
            message.AppendLine($"오셀로 게임을 시작했습니다. 흑: {black.DisplayName}, 백: {white.DisplayName}");
            message.AppendLine($"난이도: {session.Difficulty}, AI 성격: {session.AiPersonality}");

            await ProcessAutomaticTurnsAsync(session, message, cancellationToken);
            if (session.IsActive)
            {
                message.AppendLine($"현재 턴: {SideName(session.CurrentSide)} ({session.CurrentParticipant.DisplayName})");
            }
            else
            {
                message.AppendLine(BuildEndSummary(session));
                await store.RemoveAsync(session, cancellationToken);
            }

            var image = await renderer.RenderAsync(session, cancellationToken);
            return Success(
                message.ToString().Trim(),
                session,
                image,
                "오셀로 게임 시작",
                "게임이 시작되었습니다. 참여자의 봇 멘션은 종료 전까지 오셀로 진행으로 우선 처리되며, 일반 대화 일부가 제한됩니다.\n게임을 종료하려면 AI에게 기권 또는 종료를 요청하세요.");
        }
        finally
        {
            session.SyncRoot.Release();
        }
    }

    public async ValueTask<OthelloGameActionResult> MoveAsync(
        string userId,
        string channelId,
        string moveText,
        CancellationToken cancellationToken = default)
    {
        var session = store.FindActiveByUser(userId);
        if (session == null)
        {
            return Failure("진행 중인 오셀로 게임이 없습니다.");
        }

        if (session.ChannelId != channelId)
        {
            return Failure("진행 중인 오셀로 게임은 다른 채널에 있습니다. 게임을 시작한 채널에서 계속해 주세요.");
        }

        await session.SyncRoot.WaitAsync(cancellationToken);
        try
        {
            if (!session.IsActive)
            {
                await store.RemoveAsync(session, cancellationToken);
                return Failure("이미 종료된 오셀로 게임입니다.");
            }

            var side = session.SideOfUser(userId);
            if (side == null)
            {
                return Failure("이 오셀로 게임의 참여자가 아닙니다.");
            }

            if (session.CurrentParticipant.IsAi)
            {
                return Failure("아직 AI 차례입니다. 잠시 후 다시 시도해 주세요.");
            }

            if (session.CurrentParticipant.UserId != userId)
            {
                return Failure($"현재는 {session.CurrentParticipant.DisplayName} 차례입니다.");
            }

            var legalMoves = engine.GetLegalMoves(session.Board);
            if (legalMoves.Count == 0)
            {
                return Failure("현재 둘 수 있는 곳이 없습니다. 패스해야 합니다.");
            }

            var actorName = session.CurrentParticipant.DisplayName;
            if (!engine.TryApplyMove(session.Board, moveText, out var appliedMove, out var errorMessage) || appliedMove == null)
            {
                return Failure(errorMessage);
            }

            RecordMove(session, side.Value, appliedMove, actorName);
            UpdateStatus(session);

            var message = new StringBuilder();
            message.AppendLine($"{actorName}: {FormatMove(appliedMove)}");
            await ProcessAutomaticTurnsAsync(session, message, cancellationToken);

            if (session.IsActive)
            {
                message.AppendLine($"현재 턴: {SideName(session.CurrentSide)} ({session.CurrentParticipant.DisplayName})");
            }
            else
            {
                message.AppendLine(BuildEndSummary(session));
                await store.RemoveAsync(session, cancellationToken);
            }

            var image = await renderer.RenderAsync(session, cancellationToken);
            return Success(message.ToString().Trim(), session, image);
        }
        finally
        {
            session.SyncRoot.Release();
        }
    }

    public async ValueTask<OthelloGameActionResult> PassAsync(
        string userId,
        string channelId,
        CancellationToken cancellationToken = default)
    {
        var session = store.FindActiveByUser(userId);
        if (session == null)
        {
            return Failure("진행 중인 오셀로 게임이 없습니다.");
        }

        if (session.ChannelId != channelId)
        {
            return Failure("진행 중인 오셀로 게임은 다른 채널에 있습니다. 게임을 시작한 채널에서 계속해 주세요.");
        }

        await session.SyncRoot.WaitAsync(cancellationToken);
        try
        {
            var side = session.SideOfUser(userId);
            if (side == null)
            {
                return Failure("이 오셀로 게임의 참여자가 아닙니다.");
            }

            if (session.CurrentParticipant.UserId != userId)
            {
                return Failure($"현재는 {session.CurrentParticipant.DisplayName} 차례입니다.");
            }

            if (!engine.Pass(session.Board, out var errorMessage))
            {
                return Failure(errorMessage);
            }

            var message = new StringBuilder();
            RecordPass(session, side.Value, session.ParticipantOf(side.Value).DisplayName);
            UpdateStatus(session);
            message.AppendLine($"{session.ParticipantOf(side.Value).DisplayName}: 패스");
            await ProcessAutomaticTurnsAsync(session, message, cancellationToken);

            if (session.IsActive)
            {
                message.AppendLine($"현재 턴: {SideName(session.CurrentSide)} ({session.CurrentParticipant.DisplayName})");
            }
            else
            {
                message.AppendLine(BuildEndSummary(session));
                await store.RemoveAsync(session, cancellationToken);
            }

            var image = await renderer.RenderAsync(session, cancellationToken);
            return Success(message.ToString().Trim(), session, image);
        }
        finally
        {
            session.SyncRoot.Release();
        }
    }

    public async ValueTask<OthelloGameActionResult> SurrenderAsync(
        string userId,
        string channelId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var session = store.FindActiveByUser(userId);
        if (session == null)
        {
            return Failure("진행 중인 오셀로 게임이 없습니다.");
        }

        if (session.ChannelId != channelId)
        {
            return Failure("진행 중인 오셀로 게임은 다른 채널에 있습니다. 게임을 시작한 채널에서 종료해 주세요.");
        }

        await session.SyncRoot.WaitAsync(cancellationToken);
        try
        {
            var side = session.SideOfUser(userId);
            if (side == null)
            {
                return Failure("이 오셀로 게임의 참여자가 아닙니다.");
            }

            session.Status = side == OthelloSide.Black
                ? OthelloGameStatus.WhiteWon
                : OthelloGameStatus.BlackWon;
            session.UpdatedAt = DateTimeOffset.UtcNow;
            await store.RemoveAsync(session, cancellationToken);

            var image = await renderer.RenderAsync(session, cancellationToken);
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

    public async ValueTask<OthelloGameActionResult> ShowAsync(
        string userId,
        string channelId,
        CancellationToken cancellationToken = default)
    {
        var session = store.FindActiveByUser(userId);
        if (session == null)
        {
            return Failure("진행 중인 오셀로 게임이 없습니다.");
        }

        if (session.ChannelId != channelId)
        {
            return Failure("진행 중인 오셀로 게임은 다른 채널에 있습니다. 게임을 시작한 채널에서 확인해 주세요.");
        }

        await session.SyncRoot.WaitAsync(cancellationToken);
        try
        {
            var image = await renderer.RenderAsync(session, cancellationToken);
            return Success(BuildStatusMessage(session), session, image);
        }
        finally
        {
            session.SyncRoot.Release();
        }
    }

    private async ValueTask ProcessAutomaticTurnsAsync(
        OthelloGameSession session,
        StringBuilder message,
        CancellationToken cancellationToken)
    {
        for (var i = 0; i < 128; i++)
        {
            UpdateStatus(session);
            if (!session.IsActive)
            {
                return;
            }

            var legalMoves = engine.GetLegalMoves(session.Board);
            if (legalMoves.Count == 0)
            {
                var passedSide = session.CurrentSide;
                var actorName = session.CurrentParticipant.DisplayName;
                if (!engine.Pass(session.Board, out _))
                {
                    UpdateStatus(session);
                    return;
                }

                RecordPass(session, passedSide, actorName);
                message.AppendLine($"{actorName}: 둘 곳이 없어 패스");
                continue;
            }

            if (!session.CurrentParticipant.IsAi)
            {
                return;
            }

            var aiSide = session.CurrentSide;
            var aiPlayer = session.CurrentParticipant;
            var move = await opponent.ChooseMoveAsync(session, legalMoves, cancellationToken)
                ?? legalMoves[Random.Shared.Next(legalMoves.Count)];

            if (!engine.TryApplyMove(session.Board, move.Coordinate, out var appliedMove, out _)
                || appliedMove == null)
            {
                move = legalMoves[Random.Shared.Next(legalMoves.Count)];
                engine.TryApplyMove(session.Board, move.Coordinate, out appliedMove, out _);
            }

            if (appliedMove == null)
            {
                return;
            }

            RecordMove(session, aiSide, appliedMove, aiPlayer.DisplayName);
            UpdateStatus(session);
            message.AppendLine($"{aiPlayer.DisplayName}: {FormatMove(appliedMove)}");
        }
    }

    private static string? ValidateParticipants(OthelloParticipant black, OthelloParticipant white)
    {
        if (black.IsAi && white.IsAi)
        {
            return "AI끼리만 두는 게임은 지원하지 않습니다.";
        }

        if (!black.IsAi
            && !white.IsAi
            && !string.IsNullOrWhiteSpace(black.UserId)
            && black.UserId == white.UserId)
        {
            return "같은 사용자를 흑과 백으로 동시에 지정할 수 없습니다.";
        }

        return null;
    }

    private string? ValidateNoActiveGame(OthelloParticipant black, OthelloParticipant white)
    {
        foreach (var participant in new[] { black, white })
        {
            if (participant.IsAi || string.IsNullOrWhiteSpace(participant.UserId))
            {
                continue;
            }

            if (store.FindActiveByUser(participant.UserId) != null)
            {
                return $"{participant.DisplayName}님은 이미 진행 중인 오셀로 게임이 있습니다.";
            }
        }

        return null;
    }

    private void UpdateStatus(OthelloGameSession session)
    {
        session.Status = engine.GetStatus(session.Board);
        session.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static void RecordMove(
        OthelloGameSession session,
        OthelloSide side,
        OthelloMoveInfo move,
        string actorName)
    {
        session.MoveHistory.Add(new OthelloMoveRecord(
            session.MoveHistory.Count + 1,
            side,
            move.Coordinate,
            move.FlipCount,
            actorName,
            false,
            DateTimeOffset.UtcNow));
        session.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static void RecordPass(
        OthelloGameSession session,
        OthelloSide side,
        string actorName)
    {
        session.MoveHistory.Add(new OthelloMoveRecord(
            session.MoveHistory.Count + 1,
            side,
            "pass",
            0,
            actorName,
            true,
            DateTimeOffset.UtcNow));
        session.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static OthelloGameActionResult Success(
        string message,
        OthelloGameSession session,
        byte[] boardImage,
        string? systemNoticeTitle = null,
        string? systemNotice = null)
    {
        return new OthelloGameActionResult(true, message, session, boardImage, $"othello-{session.Id}.png")
        {
            IsGameOver = !session.IsActive,
            SystemNoticeTitle = systemNoticeTitle ?? (!session.IsActive ? "오셀로 게임 종료" : null),
            SystemNotice = systemNotice ?? (!session.IsActive ? "게임이 종료되었습니다. 일반 대화를 다시 사용할 수 있습니다." : null)
        };
    }

    private static OthelloGameActionResult Failure(string message)
    {
        return new OthelloGameActionResult(false, message, null, null, null);
    }

    private string BuildStatusMessage(OthelloGameSession session)
    {
        var (blackCount, whiteCount) = engine.CountDiscs(session.Board);
        var builder = new StringBuilder();
        builder.AppendLine($"흑: {session.Black.DisplayName}");
        builder.AppendLine($"백: {session.White.DisplayName}");
        builder.AppendLine($"현재 턴: {SideName(session.CurrentSide)} ({session.CurrentParticipant.DisplayName})");
        builder.AppendLine($"점수: 흑 {blackCount}, 백 {whiteCount}");
        if (session.MoveHistory.Count > 0)
        {
            var last = session.MoveHistory[^1];
            builder.AppendLine($"마지막 수: {last.ActorName} {(last.IsPass ? "패스" : last.Coordinate)}");
        }

        return builder.ToString().Trim();
    }

    private string BuildEndSummary(OthelloGameSession session, string? reason = null)
    {
        var (blackCount, whiteCount) = engine.CountDiscs(session.Board);
        var builder = new StringBuilder();
        builder.AppendLine("[오셀로 게임 종료]");
        if (!string.IsNullOrWhiteSpace(reason))
        {
            builder.AppendLine(reason.Trim());
        }

        builder.AppendLine($"결과: {BuildEndMessage(session)}");
        builder.AppendLine($"흑: {session.Black.DisplayName}");
        builder.AppendLine($"백: {session.White.DisplayName}");
        builder.AppendLine($"최종 점수: 흑 {blackCount}, 백 {whiteCount}");
        builder.AppendLine($"총 수: {session.MoveHistory.Count} ply");
        if (session.MoveHistory.Count > 0)
        {
            var last = session.MoveHistory[^1];
            builder.AppendLine($"마지막 수: {last.ActorName} {(last.IsPass ? "패스" : last.Coordinate)}");
        }

        builder.AppendLine("현재 오셀로 세션은 종료되었습니다. 새 판은 다시 오셀로를 시작하자고 말하면 시작할 수 있습니다.");
        return builder.ToString().Trim();
    }

    private string BuildEndMessage(OthelloGameSession session)
    {
        var (blackCount, whiteCount) = engine.CountDiscs(session.Board);
        return session.Status switch
        {
            OthelloGameStatus.BlackWon => $"게임 종료. 흑({session.Black.DisplayName}) 승리.",
            OthelloGameStatus.WhiteWon => $"게임 종료. 백({session.White.DisplayName}) 승리.",
            OthelloGameStatus.Draw => "게임 종료. 무승부.",
            _ => $"게임이 계속 진행 중입니다. 현재 점수: 흑 {blackCount}, 백 {whiteCount}"
        };
    }

    private static string FormatMove(OthelloMoveInfo move)
    {
        var suffix = move.FlipCount > 0
            ? $" ({move.FlipCount}개 뒤집음: {string.Join(", ", move.FlippedSquares)})"
            : string.Empty;
        return move.Coordinate + suffix;
    }

    private static string SideName(OthelloSide side) => side == OthelloSide.Black ? "흑" : "백";

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
