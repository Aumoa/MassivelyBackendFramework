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

    public Task<Settlement> CreateSettlementAsync(string ownerSubject, string title, string? description, bool allowGuestExpenseEdit = true, CancellationToken ct = default)
    {
        var settlement = new Settlement
        {
            Id = Guid.NewGuid(),
            OwnerSubject = ownerSubject,
            Title = title,
            Description = description,
            AllowGuestExpenseEdit = allowGuestExpenseEdit,
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
            MarkAiSummaryDirty(settlement);
        }
        return Task.CompletedTask;
    }

    public Task DeleteSettlementAsync(Guid id, CancellationToken ct = default)
    {
        _settlements.RemoveAll(s => s.Id == id);
        return Task.CompletedTask;
    }

    public Task SetAllowGuestExpenseEditAsync(Guid id, bool allow, CancellationToken ct = default)
    {
        var settlement = _settlements.FirstOrDefault(s => s.Id == id);
        if (settlement is not null)
            settlement.AllowGuestExpenseEdit = allow;
        return Task.CompletedTask;
    }

    public Task MarkAiSummaryDirtyAsync(Guid id, CancellationToken ct = default)
    {
        var settlement = _settlements.FirstOrDefault(s => s.Id == id);
        if (settlement is not null)
            MarkAiSummaryDirty(settlement);
        return Task.CompletedTask;
    }

    public Task<bool> SaveAiSummaryAsync(Guid id, string summary, int expectedRevision, CancellationToken ct = default)
    {
        var settlement = _settlements.FirstOrDefault(s => s.Id == id);
        if (settlement is null || settlement.AiSummaryRevision != expectedRevision || !settlement.AiSummaryDirty)
            return Task.FromResult(false);

        settlement.AiSummary = summary;
        settlement.AiSummaryDirty = false;
        settlement.AiSummaryUpdatedAt = DateTimeOffset.UtcNow;
        return Task.FromResult(true);
    }

    internal Settlement? FindSettlementByExpenseId(Guid expenseId)
    {
        return _settlements.FirstOrDefault(s => s.Expenses.Any(e => e.Id == expenseId));
    }

    internal Settlement? FindSettlementByParticipantId(Guid participantId)
    {
        return _settlements.FirstOrDefault(s => s.Participants.Any(p => p.Id == participantId));
    }

    private static void MarkAiSummaryDirty(Settlement settlement)
    {
        settlement.AiSummaryDirty = true;
        settlement.AiSummaryRevision++;
    }
}
