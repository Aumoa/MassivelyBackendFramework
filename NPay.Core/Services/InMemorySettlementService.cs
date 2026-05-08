using NPay.Models;
using NPay.Services;

namespace NPay.Core.Services;

/// <summary>
/// In-memory stub implementation of <see cref="ISettlementService"/>.
/// Replace with a MySQL-backed implementation once the schema is ready.
/// </summary>
public class InMemorySettlementService : ISettlementService
{
    private readonly List<Settlement> _settlements = [];

    public Task<IReadOnlyList<Settlement>> GetSettlementsAsync(string ownerSubject, CancellationToken ct = default)
    {
        var result = _settlements
            .Where(s => s.OwnerSubject == ownerSubject)
            .OrderByDescending(s => s.CreatedAt)
            .ToList();
        return Task.FromResult<IReadOnlyList<Settlement>>(result);
    }

    public Task<Settlement?> GetSettlementAsync(Guid id, CancellationToken ct = default)
    {
        var settlement = _settlements.FirstOrDefault(s => s.Id == id);
        return Task.FromResult(settlement);
    }

    public Task<Settlement> CreateSettlementAsync(string ownerSubject, string title, string? description, CancellationToken ct = default)
    {
        var settlement = new Settlement
        {
            Id = Guid.NewGuid(),
            OwnerSubject = ownerSubject,
            Title = title,
            Description = description,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _settlements.Add(settlement);
        return Task.FromResult(settlement);
    }

    public Task CloseSettlementAsync(Guid id, CancellationToken ct = default)
    {
        var settlement = _settlements.FirstOrDefault(s => s.Id == id);
        if (settlement is not null)
        {
            settlement.Status = SettlementStatus.Closed;
            settlement.ClosedAt = DateTimeOffset.UtcNow;
        }
        return Task.CompletedTask;
    }

    public Task DeleteSettlementAsync(Guid id, CancellationToken ct = default)
    {
        _settlements.RemoveAll(s => s.Id == id);
        return Task.CompletedTask;
    }

    /// <summary>Returns all settlements regardless of owner, for internal service use only.</summary>
    internal IReadOnlyList<Settlement> GetAllSettlements() => _settlements;
}
