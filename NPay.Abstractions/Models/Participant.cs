namespace NPay.Models;

/// <summary>
/// Represents a participant in a settlement group.
/// </summary>
public class Participant
{
    public Guid Id { get; set; }

    public Guid SettlementId { get; set; }

    /// <summary>Display name shown in the settlement UI.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Optional sub-account ID if this participant is a registered user.
    /// Null for guest participants added by name only.
    /// </summary>
    public string? Subject { get; set; }
}
