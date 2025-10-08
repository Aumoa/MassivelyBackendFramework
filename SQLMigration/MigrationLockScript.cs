using System.Data;
using Dapper;
using MySql.Data.MySqlClient;

namespace SQLMigration;

internal static class MigrationLockScript
{
    public static async ValueTask EnterAsync(MySqlConnection connection, TextWriter logger, CancellationToken cancellationToken = default)
    {
        const string QUERY1 = @";
CREATE TABLE IF NOT EXISTS `__MigrationLock` (
    `Key` INT NOT NULL PRIMARY KEY,
    `LockedAt` DATETIME NOT NULL DEFAULT NOW()
);
";

        var commandDef = new CommandDefinition(QUERY1, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(commandDef);

        while (true)
        {
            await using var tx = await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

            const string QUERY2 = @"SELECT `LockedAt` FROM `__MigrationLock` WHERE `Key` = 1";
            commandDef = new CommandDefinition(QUERY2, cancellationToken: cancellationToken);
            var lockedAt = await connection.QuerySingleOrDefaultAsync<DateTime>(commandDef);
            if (lockedAt != default && lockedAt >= DateTime.UtcNow - TimeSpan.FromMinutes(5))
            {
                logger.WriteLine("Waiting for another worker to complete migration...");
                tx.Rollback();
                await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
                continue;
            }

            const string QUERY3 = @"INSERT INTO `__MigrationLock` (`Key`) VALUES(0) ON DUPLICATE KEY UPDATE `LockedAt` = NOW()";
            commandDef = new CommandDefinition(QUERY3, cancellationToken: cancellationToken);
            await connection.ExecuteAsync(commandDef);
            await tx.CommitAsync(cancellationToken);

            break;
        }
    }

    public static async ValueTask LeaveAsync(MySqlConnection connection, CancellationToken cancellationToken = default)
    {
        const string QUERY1 = @"DELETE FROM `__MigrationLock` WHERE `Key` = 0";
        await using var tx = await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var commandDef = new CommandDefinition(QUERY1, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(commandDef);
        await tx.CommitAsync(cancellationToken);
    }
}
