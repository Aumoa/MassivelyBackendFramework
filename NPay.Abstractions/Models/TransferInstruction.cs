namespace NPay.Models;

/// <summary>
/// Represents a single transfer that one participant should make to another
/// to settle all debts within a settlement.
/// </summary>
public record TransferInstruction(
    string FromParticipantName,
    string ToParticipantName,
    decimal Amount);
