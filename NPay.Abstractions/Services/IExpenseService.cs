using NPay.Models;

namespace NPay.Services;

/// <summary>
/// Manages expense entries and calculates settlement results.
/// </summary>
public interface IExpenseService
{
    Task<Expense> AddExpenseAsync(Guid settlementId, string label, decimal amount, Guid paidByParticipantId, IEnumerable<Guid>? splitAmong = null, CancellationToken cancellationToken = default);

    Task RemoveExpenseAsync(Guid expenseId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the amount, payer, and split participants of an existing expense.
    /// Pass <see langword="null"/> for <paramref name="splitAmong"/> to reset to "all participants".
    /// </summary>
    Task UpdateExpenseAsync(Guid expenseId, decimal amount, Guid paidByParticipantId, IEnumerable<Guid>? splitAmong = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Calculates transfers needed to settle all debts.
    /// When <paramref name="minimizeTransfers"/> is true, uses greedy debt-minimization (fewer transactions).
    /// When false, each debtor pays each creditor individually per expense (exact per-expense view).
    /// </summary>
    Task<IReadOnlyList<TransferInstruction>> CalculateTransfersAsync(Guid settlementId, bool minimizeTransfers = false, CancellationToken cancellationToken = default);
}
