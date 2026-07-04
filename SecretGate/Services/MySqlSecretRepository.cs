using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;
using SecretGate.Models;
using SecretGate.Options;

namespace SecretGate.Services;

public sealed class MySqlSecretRepository(IOptions<MySqlOptions> options) : ISecretRepository
{
    public async Task<IReadOnlyList<StoredSecretRecord>> GetVaultSecretsAsync(
        string ownerSubject,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerSubject);

        using var connection = GetConnection();
        await connection.OpenAsync(cancellationToken);

        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT `id`, `name`, `salt`, `nonce`, `cipher_text`, `tag`, `created_at`, `updated_at`
FROM `secretgate_vault_secret`
WHERE `owner_subject` = @ownerSubject
ORDER BY `updated_at` DESC;";
        command.Parameters.Add("@ownerSubject", MySqlDbType.VarChar).Value = ownerSubject;

        var records = new List<StoredSecretRecord>();
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(new StoredSecretRecord(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                (byte[])reader["salt"],
                (byte[])reader["nonce"],
                (byte[])reader["cipher_text"],
                (byte[])reader["tag"],
                DateTime.SpecifyKind(reader.GetDateTime(6), DateTimeKind.Utc),
                DateTime.SpecifyKind(reader.GetDateTime(7), DateTimeKind.Utc)));
        }

        return records;
    }

    public async Task AddVaultSecretAsync(
        string ownerSubject,
        Guid id,
        string name,
        SecretEncryptionEnvelope envelope,
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerSubject);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(envelope);

        using var connection = GetConnection();
        await connection.OpenAsync(cancellationToken);

        using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO `secretgate_vault_secret`
(`id`, `owner_subject`, `name`, `salt`, `nonce`, `cipher_text`, `tag`, `created_at`, `updated_at`)
VALUES
(@id, @ownerSubject, @name, @salt, @nonce, @cipherText, @tag, @createdAt, @updatedAt);";
        command.Parameters.Add("@id", MySqlDbType.VarChar).Value = id.ToString("D");
        command.Parameters.Add("@ownerSubject", MySqlDbType.VarChar).Value = ownerSubject;
        command.Parameters.Add("@name", MySqlDbType.VarChar).Value = name;
        AddBinary(command, "@salt", envelope.Salt);
        AddBinary(command, "@nonce", envelope.Nonce);
        AddBinary(command, "@cipherText", envelope.CipherText);
        AddBinary(command, "@tag", envelope.Tag);
        command.Parameters.Add("@createdAt", MySqlDbType.DateTime).Value = nowUtc;
        command.Parameters.Add("@updatedAt", MySqlDbType.DateTime).Value = nowUtc;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteVaultSecretAsync(
        string ownerSubject,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerSubject);

        using var connection = GetConnection();
        await connection.OpenAsync(cancellationToken);

        using var command = connection.CreateCommand();
        command.CommandText = @"
DELETE FROM `secretgate_vault_secret`
WHERE `owner_subject` = @ownerSubject AND `id` = @id;";
        command.Parameters.Add("@ownerSubject", MySqlDbType.VarChar).Value = ownerSubject;
        command.Parameters.Add("@id", MySqlDbType.VarChar).Value = id.ToString("D");

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task AddShareSecretAsync(
        Guid id,
        byte[] urlTokenHash,
        byte[] accessKeyHash,
        SecretEncryptionEnvelope envelope,
        DateTime expiresAtUtc,
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(urlTokenHash);
        ArgumentNullException.ThrowIfNull(accessKeyHash);
        ArgumentNullException.ThrowIfNull(envelope);

        using var connection = GetConnection();
        await connection.OpenAsync(cancellationToken);

        using (var cleanup = connection.CreateCommand())
        {
            cleanup.CommandText = "DELETE FROM `secretgate_share_secret` WHERE `expires_at` <= @nowUtc;";
            cleanup.Parameters.Add("@nowUtc", MySqlDbType.DateTime).Value = nowUtc;
            await cleanup.ExecuteNonQueryAsync(cancellationToken);
        }

        using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO `secretgate_share_secret`
(`id`, `url_token_hash`, `access_key_hash`, `salt`, `nonce`, `cipher_text`, `tag`, `expires_at`, `created_at`)
VALUES
(@id, @urlTokenHash, @accessKeyHash, @salt, @nonce, @cipherText, @tag, @expiresAt, @createdAt);";
        command.Parameters.Add("@id", MySqlDbType.VarChar).Value = id.ToString("D");
        AddBinary(command, "@urlTokenHash", urlTokenHash);
        AddBinary(command, "@accessKeyHash", accessKeyHash);
        AddBinary(command, "@salt", envelope.Salt);
        AddBinary(command, "@nonce", envelope.Nonce);
        AddBinary(command, "@cipherText", envelope.CipherText);
        AddBinary(command, "@tag", envelope.Tag);
        command.Parameters.Add("@expiresAt", MySqlDbType.DateTime).Value = expiresAtUtc;
        command.Parameters.Add("@createdAt", MySqlDbType.DateTime).Value = nowUtc;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<SharedSecretRecord?> ConsumeShareSecretAsync(
        byte[] urlTokenHash,
        byte[] accessKeyHash,
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(urlTokenHash);
        ArgumentNullException.ThrowIfNull(accessKeyHash);

        using var connection = GetConnection();
        await connection.OpenAsync(cancellationToken);
        using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        SharedSecretRecord? record = null;
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = @"
SELECT `id`, `salt`, `nonce`, `cipher_text`, `tag`, `expires_at`, `created_at`
FROM `secretgate_share_secret`
WHERE `url_token_hash` = @urlTokenHash
  AND `access_key_hash` = @accessKeyHash
  AND `expires_at` > @nowUtc
FOR UPDATE;";
            AddBinary(command, "@urlTokenHash", urlTokenHash);
            AddBinary(command, "@accessKeyHash", accessKeyHash);
            command.Parameters.Add("@nowUtc", MySqlDbType.DateTime).Value = nowUtc;

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                record = new SharedSecretRecord(
                    Guid.Parse(reader.GetString(0)),
                    (byte[])reader["salt"],
                    (byte[])reader["nonce"],
                    (byte[])reader["cipher_text"],
                    (byte[])reader["tag"],
                    DateTime.SpecifyKind(reader.GetDateTime(5), DateTimeKind.Utc),
                    DateTime.SpecifyKind(reader.GetDateTime(6), DateTimeKind.Utc));
            }
        }

        if (record == null)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        using (var deleteCommand = connection.CreateCommand())
        {
            deleteCommand.Transaction = transaction;
            deleteCommand.CommandText = "DELETE FROM `secretgate_share_secret` WHERE `id` = @id;";
            deleteCommand.Parameters.Add("@id", MySqlDbType.VarChar).Value = record.Id.ToString("D");
            await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return record;
    }

    private MySqlConnection GetConnection()
    {
        return new MySqlConnection(options.Value.ConnectionString);
    }

    private static void AddBinary(MySqlCommand command, string name, byte[] value)
    {
        command.Parameters.Add(name, MySqlDbType.VarBinary, value.Length).Value = value;
    }
}
