using NPay.Models;

namespace NPay.Services;

/// <summary>
/// Manages settlement groups.
/// </summary>
public interface ISettlementService
{
    Task<IReadOnlyList<Settlement>> GetSettlementsAsync(string ownerSubject, CancellationToken cancellationToken = default);

    Task<Settlement?> GetSettlementAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Settlement> CreateSettlementAsync(string ownerSubject, string title, string? description, CancellationToken cancellationToken = default);

    Task CloseSettlementAsync(Guid id, CancellationToken cancellationToken = default);

    Task DeleteSettlementAsync(Guid id, CancellationToken cancellationToken = default);
}
