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
            SELECT `id`, `owner_subject`, `title`, `description`, `status`, `created_at`, `closed_at`,
                   `allow_guest_expense_edit`, `ai_summary`, `ai_summary_dirty`, `ai_summary_revision`, `ai_summary_updated_at`
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
            SELECT `id`, `owner_subject`, `title`, `description`, `status`, `created_at`, `closed_at`,
                   `allow_guest_expense_edit`, `ai_summary`, `ai_summary_dirty`, `ai_summary_revision`, `ai_summary_updated_at`
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

        var splitMap = (await conn.QueryAsync<SplitRow>(splitSql, new { settlementId = id.ToString() }))
            .GroupBy(x => x.expense_id)
            .ToDictionary(g => g.Key, g => g.Select(x => x.participant_id).ToList());

        var expenses = (await conn.QueryAsync<ExpenseRow>(expenseSql, new { settlementId = id.ToString() }))
            .Select(e => e.ToModel(splitMap))
            .ToList();

        return row.ToModel(participants, expenses);
    }

    public async Task<Settlement> CreateSettlementAsync(string ownerSubject, string title, string? description, bool allowGuestExpenseEdit = true, CancellationToken cancellationToken = default)
    {
        var settlement = new Settlement
        {
            Id = Guid.NewGuid(),
            OwnerSubject = ownerSubject,
            Title = title,
            Description = description,
            AllowGuestExpenseEdit = allowGuestExpenseEdit,
            CreatedAt = DateTimeOffset.UtcNow
        };

        const string sql = """
            INSERT INTO `npay_settlement`
                (`id`, `owner_subject`, `title`, `description`, `status`, `created_at`, `allow_guest_expense_edit`, `ai_summary_dirty`, `ai_summary_revision`)
            VALUES (@Id, @OwnerSubject, @Title, @Description, 0, @CreatedAt, @AllowGuestExpenseEdit, 1, 0)
            """;

        await using var conn = Open();
        await conn.ExecuteAsync(sql, new
        {
            Id = settlement.Id.ToString(),
            settlement.OwnerSubject,
            settlement.Title,
            settlement.Description,
            CreatedAt = settlement.CreatedAt.UtcDateTime,
            AllowGuestExpenseEdit = settlement.AllowGuestExpenseEdit ? 1 : 0
        });
        return settlement;
    }

    public async Task CloseSettlementAsync(Guid id, CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE `npay_settlement`
            SET `status` = 1,
                `closed_at` = @now,
                `ai_summary_dirty` = 1,
                `ai_summary_revision` = `ai_summary_revision` + 1
            WHERE `id` = @id
            """;
        await using var conn = Open();
        await conn.ExecuteAsync(sql, new { id = id.ToString(), now = DateTime.UtcNow });
    }

    public async Task DeleteSettlementAsync(Guid id, CancellationToken cancellationToken = default)
    {
        const string sql = "DELETE FROM `npay_settlement` WHERE `id` = @id";
        await using var conn = Open();
        await conn.ExecuteAsync(sql, new { id = id.ToString() });
    }

    public async Task SetAllowGuestExpenseEditAsync(Guid id, bool allow, CancellationToken cancellationToken = default)
    {
        const string sql = "UPDATE `npay_settlement` SET `allow_guest_expense_edit` = @allow WHERE `id` = @id";
        await using var conn = Open();
        await conn.ExecuteAsync(sql, new { id = id.ToString(), allow = allow ? 1 : 0 });
    }

    public async Task MarkAiSummaryDirtyAsync(Guid id, CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE `npay_settlement`
            SET `ai_summary_dirty` = 1,
                `ai_summary_revision` = `ai_summary_revision` + 1
            WHERE `id` = @id
            """;
        await using var conn = Open();
        await conn.ExecuteAsync(sql, new { id = id.ToString() });
    }

    public async Task<bool> SaveAiSummaryAsync(Guid id, string summary, int expectedRevision, CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE `npay_settlement`
            SET `ai_summary` = @summary,
                `ai_summary_dirty` = 0,
                `ai_summary_updated_at` = @updatedAt
            WHERE `id` = @id
              AND `ai_summary_revision` = @expectedRevision
              AND `ai_summary_dirty` = 1
            """;
        await using var conn = Open();
        var rows = await conn.ExecuteAsync(sql, new
        {
            id = id.ToString(),
            summary,
            expectedRevision,
            updatedAt = DateTime.UtcNow
        });
        return rows == 1;
    }

    // ── Row DTOs ──────────────────────────────────────────────────────────────

    private class SettlementRow
    {
        public Guid id { get; set; }
        public string owner_subject { get; set; } = "";
        public string title { get; set; } = "";
        public string? description { get; set; }
        public int status { get; set; }
        public DateTime created_at { get; set; }
        public DateTime? closed_at { get; set; }
        public bool allow_guest_expense_edit { get; set; }
        public string? ai_summary { get; set; }
        public bool ai_summary_dirty { get; set; } = true;
        public int ai_summary_revision { get; set; }
        public DateTime? ai_summary_updated_at { get; set; }

        public Settlement ToModel(IList<Participant> participants, IList<Expense> expenses) => new()
        {
            Id = id,
            OwnerSubject = owner_subject,
            Title = title,
            Description = description,
            Status = (SettlementStatus)status,
            CreatedAt = new DateTimeOffset(created_at, TimeSpan.Zero),
            ClosedAt = closed_at.HasValue ? new DateTimeOffset(closed_at.Value, TimeSpan.Zero) : null,
            AllowGuestExpenseEdit = allow_guest_expense_edit,
            AiSummary = ai_summary,
            AiSummaryDirty = ai_summary_dirty,
            AiSummaryRevision = ai_summary_revision,
            AiSummaryUpdatedAt = ai_summary_updated_at.HasValue ? new DateTimeOffset(ai_summary_updated_at.Value, TimeSpan.Zero) : null,
            Participants = participants,
            Expenses = expenses
        };
    }

    private class ParticipantRow
    {
        public Guid id { get; set; }
        public Guid settlement_id { get; set; }
        public string name { get; set; } = "";
        public string? subject { get; set; }

        public Participant ToModel() => new()
        {
            Id = id,
            SettlementId = settlement_id,
            Name = name,
            Subject = subject
        };
    }

    private class ExpenseRow
    {
        public Guid id { get; set; }
        public Guid settlement_id { get; set; }
        public string label { get; set; } = "";
        public decimal amount { get; set; }
        public Guid paid_by_participant_id { get; set; }
        public DateTime created_at { get; set; }

        public Expense ToModel(Dictionary<Guid, List<Guid>> splitMap) => new()
        {
            Id = id,
            SettlementId = settlement_id,
            Label = label,
            Amount = amount,
            PaidByParticipantId = paid_by_participant_id,
            CreatedAt = new DateTimeOffset(created_at, TimeSpan.Zero),
            SplitAmongParticipantIds = splitMap.TryGetValue(id, out var list) ? list : []
        };
    }

    private class SplitRow
    {
        public Guid expense_id { get; set; }
        public Guid participant_id { get; set; }
    }
}
