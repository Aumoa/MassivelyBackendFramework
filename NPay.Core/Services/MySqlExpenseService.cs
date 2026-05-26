using Dapper;
using MySql.Data.MySqlClient;
using NPay.Models;
using NPay.Services;

namespace NPay.Core.Services;

/// <summary>
/// MySQL-backed implementation of <see cref="IExpenseService"/>.
/// Calculation logic is identical to the in-memory version.
/// </summary>
public class MySqlExpenseService(string connectionString, MySqlSettlementService settlementService) : IExpenseService
{
    private MySqlConnection Open() => new(connectionString);

    public async Task<Expense> AddExpenseAsync(
        Guid settlementId, string label, decimal amount,
        Guid paidByParticipantId, IEnumerable<Guid>? splitAmong = null,
        CancellationToken cancellationToken = default)
    {
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

        const string expenseSql = """
            INSERT INTO `npay_expense` (`id`, `settlement_id`, `label`, `amount`, `paid_by_participant_id`, `created_at`)
            VALUES (@Id, @SettlementId, @Label, @Amount, @PaidBy, @CreatedAt)
            """;
        const string splitSql = """
            INSERT INTO `npay_expense_split` (`expense_id`, `participant_id`) VALUES (@ExpenseId, @ParticipantId)
            """;
        const string dirtySql = """
            UPDATE `npay_settlement`
            SET `ai_summary_dirty` = 1,
                `ai_summary_revision` = `ai_summary_revision` + 1
            WHERE `id` = @id
            """;

        await using var conn = Open();
        await conn.OpenAsync(cancellationToken);
        await using var transaction = await conn.BeginTransactionAsync(cancellationToken);
        await conn.ExecuteAsync(expenseSql, new
        {
            Id = expense.Id.ToString(),
            SettlementId = settlementId.ToString(),
            expense.Label,
            expense.Amount,
            PaidBy = paidByParticipantId.ToString(),
            CreatedAt = expense.CreatedAt.UtcDateTime
        }, transaction);

        foreach (var pid in expense.SplitAmongParticipantIds)
        {
            await conn.ExecuteAsync(splitSql, new
            {
                ExpenseId = expense.Id.ToString(),
                ParticipantId = pid.ToString()
            }, transaction);
        }

        await conn.ExecuteAsync(dirtySql, new { id = settlementId.ToString() }, transaction);
        await transaction.CommitAsync(cancellationToken);

        return expense;
    }

    public async Task RemoveExpenseAsync(Guid expenseId, CancellationToken cancellationToken = default)
    {
        const string settlementSql = "SELECT `settlement_id` FROM `npay_expense` WHERE `id` = @id";
        const string sql = "DELETE FROM `npay_expense` WHERE `id` = @id";
        const string dirtySql = """
            UPDATE `npay_settlement`
            SET `ai_summary_dirty` = 1,
                `ai_summary_revision` = `ai_summary_revision` + 1
            WHERE `id` = @id
            """;
        await using var conn = Open();
        await conn.OpenAsync(cancellationToken);
        await using var transaction = await conn.BeginTransactionAsync(cancellationToken);
        var settlementId = await conn.QuerySingleOrDefaultAsync<string?>(settlementSql, new { id = expenseId.ToString() }, transaction);
        await conn.ExecuteAsync(sql, new { id = expenseId.ToString() }, transaction);

        if (Guid.TryParse(settlementId, out var parsedSettlementId))
            await conn.ExecuteAsync(dirtySql, new { id = parsedSettlementId.ToString() }, transaction);

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task UpdateExpenseAsync(Guid expenseId, decimal amount, Guid paidByParticipantId, IEnumerable<Guid>? splitAmong = null, CancellationToken cancellationToken = default)
    {
        const string settlementSql = "SELECT `settlement_id` FROM `npay_expense` WHERE `id` = @id";
        const string updateSql = "UPDATE `npay_expense` SET `amount` = @amount, `paid_by_participant_id` = @paidBy WHERE `id` = @id";
        const string deleteSplitSql = "DELETE FROM `npay_expense_split` WHERE `expense_id` = @expenseId";
        const string insertSplitSql = "INSERT INTO `npay_expense_split` (`expense_id`, `participant_id`) VALUES (@expenseId, @participantId)";
        const string dirtySql = """
            UPDATE `npay_settlement`
            SET `ai_summary_dirty` = 1,
                `ai_summary_revision` = `ai_summary_revision` + 1
            WHERE `id` = @id
            """;

        await using var conn = Open();
        await conn.OpenAsync(cancellationToken);
        await using var transaction = await conn.BeginTransactionAsync(cancellationToken);
        var settlementId = await conn.QuerySingleOrDefaultAsync<string?>(settlementSql, new { id = expenseId.ToString() }, transaction);
        await conn.ExecuteAsync(updateSql, new { amount, paidBy = paidByParticipantId.ToString(), id = expenseId.ToString() }, transaction);

        await conn.ExecuteAsync(deleteSplitSql, new { expenseId = expenseId.ToString() }, transaction);

        if (splitAmong is not null)
        {
            foreach (var pid in splitAmong)
            {
                await conn.ExecuteAsync(insertSplitSql, new { expenseId = expenseId.ToString(), participantId = pid.ToString() }, transaction);
            }
        }

        if (Guid.TryParse(settlementId, out var parsedSettlementId))
            await conn.ExecuteAsync(dirtySql, new { id = parsedSettlementId.ToString() }, transaction);

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TransferInstruction>> CalculateTransfersAsync(
        Guid settlementId, bool minimizeTransfers = true, CancellationToken cancellationToken = default)
    {
        var settlement = await settlementService.GetSettlementAsync(settlementId, cancellationToken)
            ?? throw new InvalidOperationException($"Settlement {settlementId} not found.");

        var participantName = settlement.Participants.ToDictionary(p => p.Id, p => p.Name);

        if (!minimizeTransfers)
        {
            // Per-expense mode: merge transfers with the same (from, to) pair.
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
