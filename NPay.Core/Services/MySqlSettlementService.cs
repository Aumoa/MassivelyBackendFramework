using Dapper;
using MySql.Data.MySqlClient;
using NPay.Models;
using NPay.Services;

namespace NPay.Core.Services;

/// <summary>
/// MySQL-backed implementation of <see cref="ISettlementService"/>.
/// </summary>
public class MySqlSettlementService(string connectionString) : ISettlementService
{
    private MySqlConnection Open() => new(connectionString);

    public async Task<IReadOnlyList<Settlement>> GetSettlementsAsync(string ownerSubject, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT `id`, `owner_subject`, `title`, `description`, `status`, `created_at`, `closed_at`
            FROM `npay_settlement`
            WHERE `owner_subject` = @ownerSubject
            ORDER BY `created_at` DESC
            """;

        await using var conn = Open();
        var rows = await conn.QueryAsync<SettlementRow>(sql, new { ownerSubject });
        return rows.Select(r => r.ToModel([], [])).ToList();
    }

    public async Task<Settlement?> GetSettlementAsync(Guid id, CancellationToken cancellationToken = default)
    {
        const string settlementSql = """
            SELECT `id`, `owner_subject`, `title`, `description`, `status`, `created_at`, `closed_at`
            FROM `npay_settlement`
            WHERE `id` = @id
            """;
        const string participantSql = """
            SELECT `id`, `settlement_id`, `name`, `subject`
            FROM `npay_participant`
            WHERE `settlement_id` = @settlementId
            """;
        const string expenseSql = """
            SELECT `id`, `settlement_id`, `label`, `amount`, `paid_by_participant_id`, `created_at`
            FROM `npay_expense`
            WHERE `settlement_id` = @settlementId
            """;
        const string splitSql = """
            SELECT `es`.`expense_id`, `es`.`participant_id`
            FROM `npay_expense_split` `es`
            INNER JOIN `npay_expense` `e` ON `e`.`id` = `es`.`expense_id`
            WHERE `e`.`settlement_id` = @settlementId
            """;

        await using var conn = Open();
        var row = await conn.QuerySingleOrDefaultAsync<SettlementRow>(settlementSql, new { id = id.ToString() });
        if (row is null) return null;

        var participants = (await conn.QueryAsync<ParticipantRow>(participantSql, new { settlementId = id.ToString() }))
            .Select(p => p.ToModel())
            .ToList();

        var splitMap = (await conn.QueryAsync<(string expense_id, string participant_id)>(splitSql, new { settlementId = id.ToString() }))
            .GroupBy(x => x.expense_id)
            .ToDictionary(g => g.Key, g => g.Select(x => Guid.Parse(x.participant_id)).ToList());

        var expenses = (await conn.QueryAsync<ExpenseRow>(expenseSql, new { settlementId = id.ToString() }))
            .Select(e => e.ToModel(splitMap))
            .ToList();

        return row.ToModel(participants, expenses);
    }

    public async Task<Settlement> CreateSettlementAsync(string ownerSubject, string title, string? description, CancellationToken cancellationToken = default)
    {
        var settlement = new Settlement
        {
            Id = Guid.NewGuid(),
            OwnerSubject = ownerSubject,
            Title = title,
            Description = description,
            CreatedAt = DateTimeOffset.UtcNow
        };

        const string sql = """
            INSERT INTO `npay_settlement` (`id`, `owner_subject`, `title`, `description`, `status`, `created_at`)
            VALUES (@Id, @OwnerSubject, @Title, @Description, 0, @CreatedAt)
            """;

        await using var conn = Open();
        await conn.ExecuteAsync(sql, new
        {
            Id = settlement.Id.ToString(),
            settlement.OwnerSubject,
            settlement.Title,
            settlement.Description,
            CreatedAt = settlement.CreatedAt.UtcDateTime
        });
        return settlement;
    }

    public async Task CloseSettlementAsync(Guid id, CancellationToken cancellationToken = default)
    {
        const string sql = "UPDATE `npay_settlement` SET `status` = 1, `closed_at` = @now WHERE `id` = @id";
        await using var conn = Open();
        await conn.ExecuteAsync(sql, new { id = id.ToString(), now = DateTime.UtcNow });
    }

    public async Task DeleteSettlementAsync(Guid id, CancellationToken cancellationToken = default)
    {
        const string sql = "DELETE FROM `npay_settlement` WHERE `id` = @id";
        await using var conn = Open();
        await conn.ExecuteAsync(sql, new { id = id.ToString() });
    }

    // ── Row DTOs ──────────────────────────────────────────────────────────────

    private record SettlementRow(
        string id, string owner_subject, string title, string? description,
        int status, DateTime created_at, DateTime? closed_at)
    {
        public Settlement ToModel(IList<Participant> participants, IList<Expense> expenses) => new()
        {
            Id = Guid.Parse(id),
            OwnerSubject = owner_subject,
            Title = title,
            Description = description,
            Status = (SettlementStatus)status,
            CreatedAt = new DateTimeOffset(created_at, TimeSpan.Zero),
            ClosedAt = closed_at.HasValue ? new DateTimeOffset(closed_at.Value, TimeSpan.Zero) : null,
            Participants = participants,
            Expenses = expenses
        };
    }

    private record ParticipantRow(string id, string settlement_id, string name, string? subject)
    {
        public Participant ToModel() => new()
        {
            Id = Guid.Parse(id),
            SettlementId = Guid.Parse(settlement_id),
            Name = name,
            Subject = subject
        };
    }

    private record ExpenseRow(
        string id, string settlement_id, string label,
        decimal amount, string paid_by_participant_id, DateTime created_at)
    {
        public Expense ToModel(Dictionary<string, List<Guid>> splitMap) => new()
        {
            Id = Guid.Parse(id),
            SettlementId = Guid.Parse(settlement_id),
            Label = label,
            Amount = amount,
            PaidByParticipantId = Guid.Parse(paid_by_participant_id),
            CreatedAt = new DateTimeOffset(created_at, TimeSpan.Zero),
            SplitAmongParticipantIds = splitMap.TryGetValue(id, out var list) ? list : []
        };
    }
}
