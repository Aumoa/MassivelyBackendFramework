using NPay.Models;

namespace NPay.Services;

/// <summary>
/// Manages settlement groups.
/// </summary>
public interface ISettlementService
{
    Task<IReadOnlyList<Settlement>> GetSettlementsAsync(string ownerSubject, CancellationToken ct = default);

    Task<Settlement?> GetSettlementAsync(Guid id, CancellationToken ct = default);

    Task<Settlement> CreateSettlementAsync(string ownerSubject, string title, string? description, bool allowGuestExpenseEdit = true, CancellationToken ct = default);

    Task CloseSettlementAsync(Guid id, CancellationToken ct = default);

    Task DeleteSettlementAsync(Guid id, CancellationToken ct = default);

    Task SetAllowGuestExpenseEditAsync(Guid id, bool allow, CancellationToken ct = default);

    Task MarkAiSummaryDirtyAsync(Guid id, CancellationToken ct = default);

    Task<bool> SaveAiSummaryAsync(Guid id, string summary, int expectedRevision, CancellationToken ct = default);
}
