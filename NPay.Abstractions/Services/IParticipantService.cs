using NPay.Models;

namespace NPay.Services;

/// <summary>
/// Manages participants within a settlement.
/// </summary>
public interface IParticipantService
{
    Task<Participant> AddParticipantAsync(Guid settlementId, string name, string? subject = null, CancellationToken ct = default);

    Task RemoveParticipantAsync(Guid participantId, CancellationToken ct = default);
}
