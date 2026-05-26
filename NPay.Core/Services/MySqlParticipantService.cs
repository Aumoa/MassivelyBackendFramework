using Dapper;
using MySql.Data.MySqlClient;
using NPay.Models;
using NPay.Services;

namespace NPay.Core.Services;

/// <summary>
/// MySQL-backed implementation of <see cref="IParticipantService"/>.
/// </summary>
public class MySqlParticipantService(string connectionString) : IParticipantService
{
    private MySqlConnection Open() => new(connectionString);

    public async Task<Participant> AddParticipantAsync(
        Guid settlementId, string name, string? subject = null,
        CancellationToken cancellationToken = default)
    {
        var participant = new Participant
        {
            Id = Guid.NewGuid(),
            SettlementId = settlementId,
            Name = name,
            Subject = subject
        };

        const string sql = """
            INSERT INTO `npay_participant` (`id`, `settlement_id`, `name`, `subject`)
            VALUES (@Id, @SettlementId, @Name, @Subject)
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
        await conn.ExecuteAsync(sql, new
        {
            Id = participant.Id.ToString(),
            SettlementId = settlementId.ToString(),
            participant.Name,
            participant.Subject
        }, transaction);

        await conn.ExecuteAsync(dirtySql, new { id = settlementId.ToString() }, transaction);
        await transaction.CommitAsync(cancellationToken);

        return participant;
    }

    public async Task RemoveParticipantAsync(Guid participantId, CancellationToken cancellationToken = default)
    {
        const string settlementSql = "SELECT `settlement_id` FROM `npay_participant` WHERE `id` = @id";
        const string sql = "DELETE FROM `npay_participant` WHERE `id` = @id";
        const string dirtySql = """
            UPDATE `npay_settlement`
            SET `ai_summary_dirty` = 1,
                `ai_summary_revision` = `ai_summary_revision` + 1
            WHERE `id` = @id
            """;
        await using var conn = Open();
        await conn.OpenAsync(cancellationToken);
        await using var transaction = await conn.BeginTransactionAsync(cancellationToken);
        var settlementId = await conn.QuerySingleOrDefaultAsync<string?>(settlementSql, new { id = participantId.ToString() }, transaction);
        await conn.ExecuteAsync(sql, new { id = participantId.ToString() }, transaction);

        if (Guid.TryParse(settlementId, out var parsedSettlementId))
            await conn.ExecuteAsync(dirtySql, new { id = parsedSettlementId.ToString() }, transaction);

        await transaction.CommitAsync(cancellationToken);
    }
}
