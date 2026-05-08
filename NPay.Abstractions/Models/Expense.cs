namespace NPay.Models;

/// <summary>
/// Represents a single expense entry within a settlement.
/// </summary>
public class Expense
{
    public Guid Id { get; set; }

    public Guid SettlementId { get; set; }

    /// <summary>Short label for this expense (e.g., "Beer", "Taxi").</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Total amount paid for this expense.</summary>
    public decimal Amount { get; set; }

    /// <summary>Participant ID who paid this expense.</summary>
    public Guid PaidByParticipantId { get; set; }

    /// <summary>
    /// Participant IDs who share this expense.
    /// When empty, the expense is split equally among all participants.
    /// </summary>
    public IList<Guid> SplitAmongParticipantIds { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }
}
