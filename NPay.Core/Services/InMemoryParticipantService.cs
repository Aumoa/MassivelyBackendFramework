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
        return participant;
    }

    public async Task RemoveParticipantAsync(Guid participantId, CancellationToken cancellationToken = default)
    {
        foreach (var s in (await settlements.GetSettlementsAsync(string.Empty, cancellationToken)).ToList())
        {
            var p = s.Participants.FirstOrDefault(p => p.Id == participantId);
            if (p is not null)
            {
                s.Participants.Remove(p);
                return;
            }
        }
    }
}
