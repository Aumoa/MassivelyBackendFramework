using NPay.Models;
using NPay.Services;

namespace NPay.Core.Services;

/// <summary>
/// In-memory stub implementation of <see cref="IExpenseService"/>.
/// Includes a greedy debt-minimization algorithm for transfer calculation.
/// </summary>
public class InMemoryExpenseService(InMemorySettlementService settlements) : IExpenseService
{
    public async Task<Expense> AddExpenseAsync(
        Guid settlementId,
        string label,
        decimal amount,
        Guid paidByParticipantId,
        IEnumerable<Guid>? splitAmong = null,
        CancellationToken cancellationToken = default)
    {
        var settlement = await settlements.GetSettlementAsync(settlementId, cancellationToken)
            ?? throw new InvalidOperationException($"Settlement {settlementId} not found.");

        var expense = new Expense
        {
            Id = Guid.NewGuid(),
            SettlementId = settlementId,
            Label = label,
            Amount = amount,
            PaidByParticipantId = paidByParticipantId,
            SplitAmongParticipantIds = splitAmong?.ToList() ?? [],
            CreatedAt = DateTimeOffset.UtcNow
        };
        settlement.Expenses.Add(expense);
        return expense;
    }

    public async Task RemoveExpenseAsync(Guid expenseId, CancellationToken cancellationToken = default)
    {
        foreach (var s in (await settlements.GetSettlementsAsync(string.Empty, cancellationToken)).ToList())
        {
            var e = s.Expenses.FirstOrDefault(e => e.Id == expenseId);
            if (e is not null)
            {
                s.Expenses.Remove(e);
                return;
            }
        }
    }

    public async Task UpdateExpenseAsync(Guid expenseId, decimal amount, Guid paidByParticipantId, IEnumerable<Guid>? splitAmong = null, CancellationToken cancellationToken = default)
    {
        foreach (var s in (await settlements.GetSettlementsAsync(string.Empty, cancellationToken)).ToList())
        {
            var e = s.Expenses.FirstOrDefault(e => e.Id == expenseId);
            if (e is not null)
            {
                e.Amount = amount;
                e.PaidByParticipantId = paidByParticipantId;
                e.SplitAmongParticipantIds = splitAmong?.ToList() ?? [];
                return;
            }
        }
    }

    public async Task<IReadOnlyList<TransferInstruction>> CalculateTransfersAsync(Guid settlementId, bool minimizeTransfers = false, CancellationToken cancellationToken = default)
    {
        var settlement = await settlements.GetSettlementAsync(settlementId, cancellationToken)
            ?? throw new InvalidOperationException($"Settlement {settlementId} not found.");

        // Build a balance map: positive = owed to this participant, negative = owes others.
        var balance = settlement.Participants.ToDictionary(p => p.Id, _ => 0m);

        foreach (var expense in settlement.Expenses)
        {
            var splitIds = expense.SplitAmongParticipantIds.Count > 0
                ? expense.SplitAmongParticipantIds
                : settlement.Participants.Select(p => p.Id).ToList();

            if (!balance.ContainsKey(expense.PaidByParticipantId)) continue;
            balance[expense.PaidByParticipantId] += expense.Amount;

            var share = expense.Amount / splitIds.Count;
            foreach (var pid in splitIds)
            {
                if (balance.ContainsKey(pid))
                    balance[pid] -= share;
            }
        }

        // Greedy minimization: pair the largest creditor with the largest debtor.
        var creditors = new SortedList<decimal, Queue<Guid>>();
        var debtors = new SortedList<decimal, Queue<Guid>>();

        foreach (var (pid, bal) in balance)
        {
            if (bal > 0.01m) Enqueue(creditors, bal, pid);
            else if (bal < -0.01m) Enqueue(debtors, -bal, pid);
        }

        var result = new List<TransferInstruction>();
        var participantName = settlement.Participants.ToDictionary(p => p.Id, p => p.Name);

        while (creditors.Count > 0 && debtors.Count > 0)
        {
            var (cred, creditorId) = Dequeue(creditors);
            var (debt, debtorId) = Dequeue(debtors);

            var transfer = Math.Min(cred, debt);
            result.Add(new TransferInstruction(
                participantName.GetValueOrDefault(debtorId, "?"),
                participantName.GetValueOrDefault(creditorId, "?"),
                Math.Round(transfer, 0)));

            if (cred - transfer > 0.01m) Enqueue(creditors, cred - transfer, creditorId);
            if (debt - transfer > 0.01m) Enqueue(debtors, debt - transfer, debtorId);
        }

        return result;

        static void Enqueue(SortedList<decimal, Queue<Guid>> list, decimal key, Guid id)
        {
            if (!list.TryGetValue(key, out var q))
            {
                q = new Queue<Guid>();
                list[key] = q;
            }
            q.Enqueue(id);
        }

        static (decimal key, Guid id) Dequeue(SortedList<decimal, Queue<Guid>> list)
        {
            var key = list.Keys[^1];
            var q = list.Values[^1];
            var id = q.Dequeue();
            if (q.Count == 0) list.RemoveAt(list.Count - 1);
            return (key, id);
        }
    }
}
