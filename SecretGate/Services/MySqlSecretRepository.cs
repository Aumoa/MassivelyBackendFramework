using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;
using SecretGate.Models;
using SecretGate.Options;

namespace SecretGate.Services;

public sealed class MySqlSecretRepository(IOptions<MySqlOptions> options) : ISecretRepository
{
    public async Task<VaultProfileRecord?> GetVaultProfileAsync(
        string ownerSubject,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerSubject);

        using var connection = GetConnection();
        await connection.OpenAsync(cancellationToken);

        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT `owner_subject`, `salt`, `nonce`, `cipher_text`, `tag`, `created_at`, `updated_at`
FROM `secretgate_vault_profile`
WHERE `owner_subject` = @ownerSubject;";
        command.Parameters.Add("@ownerSubject", MySqlDbType.VarChar).Value = ownerSubject;

        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new VaultProfileRecord(
            reader.GetString(0),
            (byte[])reader["salt"],
            (byte[])reader["nonce"],
            (byte[])reader["cipher_text"],
            (byte[])reader["tag"],
            DateTime.SpecifyKind(reader.GetDateTime(5), DateTimeKind.Utc),
            DateTime.SpecifyKind(reader.GetDateTime(6), DateTimeKind.Utc));
    }

    public async Task InitializeVaultAsync(
        string ownerSubject,
        SecretEncryptionEnvelope profileEnvelope,
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerSubject);
        ArgumentNullException.ThrowIfNull(profileEnvelope);

        using var connection = GetConnection();
        await connection.OpenAsync(cancellationToken);
        using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        using (var deleteSecrets = connection.CreateCommand())
        {
            deleteSecrets.Transaction = transaction;
            deleteSecrets.CommandText = "DELETE FROM `secretgate_vault_secret` WHERE `owner_subject` = @ownerSubject;";
            deleteSecrets.Parameters.Add("@ownerSubject", MySqlDbType.VarChar).Value = ownerSubject;
            await deleteSecrets.ExecuteNonQueryAsync(cancellationToken);
        }

        using (var deleteProfile = connection.CreateCommand())
        {
            deleteProfile.Transaction = transaction;
            deleteProfile.CommandText = "DELETE FROM `secretgate_vault_profile` WHERE `owner_subject` = @ownerSubject;";
            deleteProfile.Parameters.Add("@ownerSubject", MySqlDbType.VarChar).Value = ownerSubject;
            await deleteProfile.ExecuteNonQueryAsync(cancellationToken);
        }

        using (var insertProfile = connection.CreateCommand())
        {
            insertProfile.Transaction = transaction;
            insertProfile.CommandText = @"
INSERT INTO `secretgate_vault_profile`
(`owner_subject`, `salt`, `nonce`, `cipher_text`, `tag`, `created_at`, `updated_at`)
VALUES
(@ownerSubject, @salt, @nonce, @cipherText, @tag, @createdAt, @updatedAt);";
            insertProfile.Parameters.Add("@ownerSubject", MySqlDbType.VarChar).Value = ownerSubject;
            AddBinary(insertProfile, "@salt", profileEnvelope.Salt);
            AddBinary(insertProfile, "@nonce", profileEnvelope.Nonce);
            AddBinary(insertProfile, "@cipherText", profileEnvelope.CipherText);
            AddBinary(insertProfile, "@tag", profileEnvelope.Tag);
            insertProfile.Parameters.Add("@createdAt", MySqlDbType.DateTime).Value = nowUtc;
            insertProfile.Parameters.Add("@updatedAt", MySqlDbType.DateTime).Value = nowUtc;
            await insertProfile.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task ReplaceVaultEncryptionAsync(
        string ownerSubject,
        SecretEncryptionEnvelope profileEnvelope,
        IReadOnlyList<VaultSecretEncryptionUpdate> secretUpdates,
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerSubject);
        ArgumentNullException.ThrowIfNull(profileEnvelope);
        ArgumentNullException.ThrowIfNull(secretUpdates);

        using var connection = GetConnection();
        await connection.OpenAsync(cancellationToken);
        using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        using (var updateProfile = connection.CreateCommand())
        {
            updateProfile.Transaction = transaction;
            updateProfile.CommandText = @"
UPDATE `secretgate_vault_profile`
SET `salt` = @salt,
    `nonce` = @nonce,
    `cipher_text` = @cipherText,
    `tag` = @tag,
    `updated_at` = @updatedAt
WHERE `owner_subject` = @ownerSubject;";
            updateProfile.Parameters.Add("@ownerSubject", MySqlDbType.VarChar).Value = ownerSubject;
            AddBinary(updateProfile, "@salt", profileEnvelope.Salt);
            AddBinary(updateProfile, "@nonce", profileEnvelope.Nonce);
            AddBinary(updateProfile, "@cipherText", profileEnvelope.CipherText);
            AddBinary(updateProfile, "@tag", profileEnvelope.Tag);
            updateProfile.Parameters.Add("@updatedAt", MySqlDbType.DateTime).Value = nowUtc;
            await updateProfile.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var update in secretUpdates)
        {
            using var updateSecret = connection.CreateCommand();
            updateSecret.Transaction = transaction;
            updateSecret.CommandText = @"
UPDATE `secretgate_vault_secret`
SET `salt` = @salt,
    `nonce` = @nonce,
    `cipher_text` = @cipherText,
    `tag` = @tag,
    `updated_at` = @updatedAt
WHERE `owner_subject` = @ownerSubject AND `id` = @id;";
            updateSecret.Parameters.Add("@ownerSubject", MySqlDbType.VarChar).Value = ownerSubject;
            updateSecret.Parameters.Add("@id", MySqlDbType.VarChar).Value = update.Id.ToString("D");
            AddBinary(updateSecret, "@salt", update.Envelope.Salt);
            AddBinary(updateSecret, "@nonce", update.Envelope.Nonce);
            AddBinary(updateSecret, "@cipherText", update.Envelope.CipherText);
            AddBinary(updateSecret, "@tag", update.Envelope.Tag);
            updateSecret.Parameters.Add("@updatedAt", MySqlDbType.DateTime).Value = nowUtc;
            await updateSecret.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task ResetVaultAsync(
        string ownerSubject,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerSubject);

        using var connection = GetConnection();
        await connection.OpenAsync(cancellationToken);
        using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        using (var deleteSecrets = connection.CreateCommand())
        {
            deleteSecrets.Transaction = transaction;
            deleteSecrets.CommandText = "DELETE FROM `secretgate_vault_secret` WHERE `owner_subject` = @ownerSubject;";
            deleteSecrets.Parameters.Add("@ownerSubject", MySqlDbType.VarChar).Value = ownerSubject;
            await deleteSecrets.ExecuteNonQueryAsync(cancellationToken);
        }

        using (var deleteProfile = connection.CreateCommand())
        {
            deleteProfile.Transaction = transaction;
            deleteProfile.CommandText = "DELETE FROM `secretgate_vault_profile` WHERE `owner_subject` = @ownerSubject;";
            deleteProfile.Parameters.Add("@ownerSubject", MySqlDbType.VarChar).Value = ownerSubject;
            await deleteProfile.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

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
