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
        CancellationToken ct = default)
    {
        var settlement = await settlements.GetSettlementAsync(settlementId, ct)
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

    public Task RemoveExpenseAsync(Guid expenseId, CancellationToken ct = default)
    {
        foreach (var s in settlements.GetAllSettlements())
        {
            var e = s.Expenses.FirstOrDefault(e => e.Id == expenseId);
            if (e is not null)
            {
                s.Expenses.Remove(e);
                return Task.CompletedTask;
            }
        }
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<TransferInstruction>> CalculateTransfersAsync(Guid settlementId, bool minimizeTransfers = false, CancellationToken ct = default)
    {
        var settlement = await settlements.GetSettlementAsync(settlementId, ct)
            ?? throw new InvalidOperationException($"Settlement {settlementId} not found.");

        var participantName = settlement.Participants.ToDictionary(p => p.Id, p => p.Name);

        if (!minimizeTransfers)
        {
            // Per-expense mode: each debtor pays the payer directly for each expense.
            // Entries with the same (from, to) pair are merged into a single transfer.
            var grouped = new Dictionary<(Guid from, Guid to), decimal>();
            foreach (var expense in settlement.Expenses)
            {
                var splitIds = expense.SplitAmongParticipantIds.Count > 0
                    ? expense.SplitAmongParticipantIds
                    : settlement.Participants.Select(p => p.Id).ToList();

                var share = Math.Round(expense.Amount / splitIds.Count, 0);
                foreach (var pid in splitIds)
                {
                    if (pid == expense.PaidByParticipantId) continue;
                    if (!participantName.ContainsKey(pid)) continue;
                    var key = (pid, expense.PaidByParticipantId);
                    grouped[key] = grouped.GetValueOrDefault(key, 0m) + share;
                }
            }

            return grouped
                .Select(kv => new TransferInstruction(
                    participantName.GetValueOrDefault(kv.Key.from, "?"),
                    participantName.GetValueOrDefault(kv.Key.to, "?"),
                    kv.Value))
                .ToList();
        }

        // Greedy minimization mode: build balance map then minimize transfer count.
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

        var creditors = new SortedList<decimal, Queue<Guid>>();
        var debtors = new SortedList<decimal, Queue<Guid>>();

        foreach (var (pid, bal) in balance)
        {
            if (bal > 0.01m) Enqueue(creditors, bal, pid);
            else if (bal < -0.01m) Enqueue(debtors, -bal, pid);
        }

        var minimized = new List<TransferInstruction>();

        while (creditors.Count > 0 && debtors.Count > 0)
        {
            var (cred, creditorId) = Dequeue(creditors);
            var (debt, debtorId) = Dequeue(debtors);

            var transfer = Math.Min(cred, debt);
            minimized.Add(new TransferInstruction(
                participantName.GetValueOrDefault(debtorId, "?"),
                participantName.GetValueOrDefault(creditorId, "?"),
                Math.Round(transfer, 0)));

            if (cred - transfer > 0.01m) Enqueue(creditors, cred - transfer, creditorId);
            if (debt - transfer > 0.01m) Enqueue(debtors, debt - transfer, debtorId);
        }

        return minimized;

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
