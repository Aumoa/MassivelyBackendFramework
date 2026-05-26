using NPay.Models;
using NPay.Services;

namespace NPay.Core.Services;

/// <summary>
/// In-memory stub implementation of <see cref="IParticipantService"/>.
/// </summary>
public class InMemoryParticipantService(InMemorySettlementService settlements) : IParticipantService
{
    public async Task<Participant> AddParticipantAsync(Guid settlementId, string name, string? subject = null, CancellationToken cancellationToken = default)
    {
        var settlement = await settlements.GetSettlementAsync(settlementId, cancellationToken)
            ?? throw new InvalidOperationException($"Settlement {settlementId} not found.");

        var participant = new Participant
        {
            Id = Guid.NewGuid(),
            SettlementId = settlementId,
            Name = name,
            Subject = subject
        };
        settlement.Participants.Add(participant);
        await settlements.MarkAiSummaryDirtyAsync(settlementId, cancellationToken);
        return participant;
    }

    public Task RemoveParticipantAsync(Guid participantId, CancellationToken cancellationToken = default)
    {
        var settlement = settlements.FindSettlementByParticipantId(participantId);
        if (settlement is not null)
        {
            var p = settlement.Participants.FirstOrDefault(p => p.Id == participantId);
            if (p is not null)
            {
                settlement.Participants.Remove(p);
                return settlements.MarkAiSummaryDirtyAsync(settlement.Id, cancellationToken);
            }
        }

        return Task.CompletedTask;
    }
}
