using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;
using SecretGate.Models;
using SecretGate.Options;

namespace SecretGate.Services;

public sealed class MySqlSecretRepository(IOptions<MySqlOptions> options) : ISecretRepository
{
    internal const int MaxShareAccessFailures = 8;
    private const int DuplicateKeyErrorNumber = 1062;

    public async Task<VaultProfileRecord?> GetVaultProfileAsync(
        string ownerSubject,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerSubject);

        using var connection = GetConnection();
        await connection.OpenAsync(cancellationToken);

        return await ReadVaultProfileAsync(
            connection,
            transaction: null,
            ownerSubject,
            forUpdate: false,
            cancellationToken);
    }

    public async Task<bool> InitializeVaultAsync(
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

        var existingProfile = await ReadVaultProfileAsync(
            connection,
            transaction,
            ownerSubject,
            forUpdate: true,
            cancellationToken);
        if (existingProfile != null)
        {
            await transaction.CommitAsync(cancellationToken);
            return false;
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
            try
            {
                await insertProfile.ExecuteNonQueryAsync(cancellationToken);
            }
            catch (MySqlException ex) when (ex.Number == DuplicateKeyErrorNumber)
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<bool> ReplaceVaultEncryptionAsync(
        string ownerSubject,
        Func<VaultProfileRecord, IReadOnlyList<StoredSecretRecord>, VaultEncryptionReplacement?> replacementFactory,
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerSubject);
        ArgumentNullException.ThrowIfNull(replacementFactory);

        using var connection = GetConnection();
        await connection.OpenAsync(cancellationToken);
        using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var profile = await ReadVaultProfileAsync(
            connection,
            transaction,
            ownerSubject,
            forUpdate: true,
            cancellationToken);
        if (profile == null)
        {
            await transaction.CommitAsync(cancellationToken);
            return false;
        }

        var records = await ReadVaultSecretsAsync(connection, transaction, ownerSubject, cancellationToken);
        var replacement = replacementFactory(profile, records);
        if (replacement == null)
        {
            await transaction.CommitAsync(cancellationToken);
            return false;
        }

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
            AddBinary(updateProfile, "@salt", replacement.ProfileEnvelope.Salt);
            AddBinary(updateProfile, "@nonce", replacement.ProfileEnvelope.Nonce);
            AddBinary(updateProfile, "@cipherText", replacement.ProfileEnvelope.CipherText);
            AddBinary(updateProfile, "@tag", replacement.ProfileEnvelope.Tag);
            updateProfile.Parameters.Add("@updatedAt", MySqlDbType.DateTime).Value = nowUtc;
            await updateProfile.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var update in replacement.SecretUpdates)
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
        return true;
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

        return await ReadVaultSecretsAsync(connection, transaction: null, ownerSubject, cancellationToken);
    }

    public async Task<bool> AddVaultSecretAsync(
        string ownerSubject,
        Func<VaultProfileRecord, bool> profileValidator,
        Guid id,
        string name,
        SecretEncryptionEnvelope envelope,
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerSubject);
        ArgumentNullException.ThrowIfNull(profileValidator);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(envelope);

        using var connection = GetConnection();
        await connection.OpenAsync(cancellationToken);
        using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var profile = await ReadVaultProfileAsync(
            connection,
            transaction,
            ownerSubject,
            forUpdate: true,
            cancellationToken);
        if (profile == null || !profileValidator(profile))
        {
            await transaction.CommitAsync(cancellationToken);
            return false;
        }

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
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
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteVaultSecretAsync(
        string ownerSubject,
        Func<VaultProfileRecord, bool> profileValidator,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerSubject);
        ArgumentNullException.ThrowIfNull(profileValidator);

        using var connection = GetConnection();
        await connection.OpenAsync(cancellationToken);
        using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var profile = await ReadVaultProfileAsync(
            connection,
            transaction,
            ownerSubject,
            forUpdate: true,
            cancellationToken);
        if (profile == null || !profileValidator(profile))
        {
            await transaction.CommitAsync(cancellationToken);
            return false;
        }

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
DELETE FROM `secretgate_vault_secret`
WHERE `owner_subject` = @ownerSubject AND `id` = @id;";
        command.Parameters.Add("@ownerSubject", MySqlDbType.VarChar).Value = ownerSubject;
        command.Parameters.Add("@id", MySqlDbType.VarChar).Value = id.ToString("D");

        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
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
SELECT `id`, `access_key_hash`, `failed_access_attempts`, `salt`, `nonce`, `cipher_text`, `tag`, `expires_at`, `created_at`
FROM `secretgate_share_secret`
WHERE `url_token_hash` = @urlTokenHash
  AND `expires_at` > @nowUtc
FOR UPDATE;";
            AddBinary(command, "@urlTokenHash", urlTokenHash);
            command.Parameters.Add("@nowUtc", MySqlDbType.DateTime).Value = nowUtc;

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                var storedAccessKeyHash = (byte[])reader["access_key_hash"];
                if (CryptographicOperations.FixedTimeEquals(storedAccessKeyHash, accessKeyHash))
                {
                    record = new SharedSecretRecord(
                        ReadGuid(reader.GetValue(0)),
                        (byte[])reader["salt"],
                        (byte[])reader["nonce"],
                        (byte[])reader["cipher_text"],
                        (byte[])reader["tag"],
                        DateTime.SpecifyKind(reader.GetDateTime(7), DateTimeKind.Utc),
                        DateTime.SpecifyKind(reader.GetDateTime(8), DateTimeKind.Utc));
                }
                else
                {
                    var id = ReadGuid(reader.GetValue(0));
                    var nextFailedAttempts = reader.GetInt32(2) + 1;
                    await reader.DisposeAsync();
                    await RecordShareAccessFailureAsync(
                        connection,
                        transaction,
                        id,
                        nextFailedAttempts,
                        nowUtc,
                        cancellationToken);

                    await transaction.CommitAsync(cancellationToken);
                    return null;
                }
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

    private static async Task RecordShareAccessFailureAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        Guid id,
        int failedAttempts,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.Parameters.Add("@id", MySqlDbType.VarChar).Value = id.ToString("D");
        if (failedAttempts >= MaxShareAccessFailures)
        {
            command.CommandText = "DELETE FROM `secretgate_share_secret` WHERE `id` = @id;";
        }
        else
        {
            command.CommandText = @"
UPDATE `secretgate_share_secret`
SET `failed_access_attempts` = @failedAttempts,
    `last_failed_at` = @nowUtc
WHERE `id` = @id;";
            command.Parameters.Add("@failedAttempts", MySqlDbType.Int32).Value = failedAttempts;
            command.Parameters.Add("@nowUtc", MySqlDbType.DateTime).Value = nowUtc;
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<VaultProfileRecord?> ReadVaultProfileAsync(
        MySqlConnection connection,
        MySqlTransaction? transaction,
        string ownerSubject,
        bool forUpdate,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
SELECT `owner_subject`, `salt`, `nonce`, `cipher_text`, `tag`, `created_at`, `updated_at`
FROM `secretgate_vault_profile`
WHERE `owner_subject` = @ownerSubject" + (forUpdate ? "\nFOR UPDATE;" : ";");
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

    private static async Task<IReadOnlyList<StoredSecretRecord>> ReadVaultSecretsAsync(
        MySqlConnection connection,
        MySqlTransaction? transaction,
        string ownerSubject,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
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
                ReadGuid(reader.GetValue(0)),
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

    private MySqlConnection GetConnection()
    {
        return new MySqlConnection(options.Value.ConnectionString);
    }

    internal static Guid ReadGuid(object value)
    {
        if (value is null || value == DBNull.Value)
        {
            throw new InvalidCastException("Cannot convert null database value to Guid.");
        }

        if (value is Guid guid)
        {
            return guid;
        }

        if (value is string text)
        {
            return Guid.Parse(text);
        }

        if (value is byte[] bytes)
        {
            if (bytes.Length == 16)
            {
                return new Guid(bytes);
            }

            return Guid.Parse(Encoding.UTF8.GetString(bytes));
        }

        throw new InvalidCastException($"Cannot convert database value of type '{value.GetType().FullName}' to Guid.");
    }

    private static void AddBinary(MySqlCommand command, string name, byte[] value)
    {
        command.Parameters.Add(name, MySqlDbType.VarBinary, value.Length).Value = value;
    }
}
