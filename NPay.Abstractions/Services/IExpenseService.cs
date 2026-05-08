using NPay.Models;

namespace NPay.Services;

/// <summary>
/// Manages expense entries and calculates settlement results.
/// </summary>
public interface IExpenseService
{
    Task<Expense> AddExpenseAsync(Guid settlementId, string label, decimal amount, Guid paidByParticipantId, IEnumerable<Guid>? splitAmong = null, CancellationToken ct = default);

    Task RemoveExpenseAsync(Guid expenseId, CancellationToken ct = default);

    /// <summary>
    /// Calculates the minimum set of transfers needed to settle all debts.
    /// </summary>
    Task<IReadOnlyList<TransferInstruction>> CalculateTransfersAsync(Guid settlementId, CancellationToken ct = default);
}
