namespace NPay.Models;

/// <summary>
/// Represents the state of a settlement group.
/// </summary>
public enum SettlementStatus
{
    /// <summary>Settlement is open and expenses can be added.</summary>
    Open,

    /// <summary>Settlement is closed and the result is finalized.</summary>
    Closed
}

/// <summary>
/// Represents a settlement group (e.g., a dinner outing).
/// </summary>
public class Settlement
{
    public Guid Id { get; set; }

    /// <summary>Display name of the settlement (e.g., "Friday dinner").</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Optional description or memo.</summary>
    public string? Description { get; set; }

    public SettlementStatus Status { get; set; } = SettlementStatus.Open;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? ClosedAt { get; set; }

    /// <summary>Sub-account ID of the user who created this settlement.</summary>
    public string OwnerSubject { get; set; } = string.Empty;

    /// <summary>
    /// When true, anyone who has the settlement link (including unauthenticated users)
    /// may add or remove expenses. Owner-only actions (participants, closing, deletion) are
    /// unaffected.
    /// </summary>
    public bool AllowGuestExpenseEdit { get; set; } = true;

    public IList<Participant> Participants { get; set; } = [];

    public IList<Expense> Expenses { get; set; } = [];
}
