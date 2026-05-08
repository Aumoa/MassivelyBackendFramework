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
        Guid settlementId, string name, string? subject = null, CancellationToken cancellationToken = default)
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

        await using var conn = Open();
        await conn.ExecuteAsync(sql, new
        {
            Id = participant.Id.ToString(),
            SettlementId = settlementId.ToString(),
            participant.Name,
            participant.Subject
        });
        return participant;
    }

    public async Task RemoveParticipantAsync(Guid participantId, CancellationToken cancellationToken = default)
    {
        const string sql = "DELETE FROM `npay_participant` WHERE `id` = @id";
        await using var conn = Open();
        await conn.ExecuteAsync(sql, new { id = participantId.ToString() });
    }
}
