using NPay.Models;

namespace NPay.Services;

/// <summary>
/// Manages settlement groups.
/// </summary>
public interface ISettlementService
{
    Task<IReadOnlyList<Settlement>> GetSettlementsAsync(string ownerSubject, CancellationToken ct = default);

    Task<Settlement?> GetSettlementAsync(Guid id, CancellationToken ct = default);

    Task<Settlement> CreateSettlementAsync(string ownerSubject, string title, string? description, CancellationToken ct = default);

    Task CloseSettlementAsync(Guid id, CancellationToken ct = default);

    Task DeleteSettlementAsync(Guid id, CancellationToken ct = default);
}
