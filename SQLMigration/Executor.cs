using System.Text;
using Dapper;
using MySql.Data.MySqlClient;

namespace SQLMigration;

public static class Executor
{
    public static async ValueTask RunAsync(string connectionString, string databaseName, IScript[] scripts, TextWriter logger, CancellationToken cancellationToken = default)
    {
        using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        string QUERY1 = @$"
CREATE SCHEMA IF NOT EXISTS `{databaseName}`;
USE `{databaseName}`;
";

        var commandDef = new CommandDefinition(QUERY1, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(commandDef);

        await MigrationLockScript.EnterAsync(connection, logger, cancellationToken);
        try
        {
            var installedItems = await MigrationTableScript.ReadAsync(connection, cancellationToken);
            StringBuilder? sb = null;
            foreach (var installed in installedItems)
            {
                if (installed.Status is ExecutionStatus.UpFailed or ExecutionStatus.DownFailed)
                {
                    sb ??= new StringBuilder();
                    sb.AppendLine($"Migration '{installed.Name}' with rank {installed.InstalledRank} has failed with status '{installed.Status}'.");
                }
            }

            if (sb != null)
            {
                throw new InvalidOperationException(sb.ToString() + "One or more failed migration items exist. You must resolve these migration failures manually. After resolving, you must also update the values in the __MigrationHistory table accordingly.");
            }

            scripts = [.. from s in scripts
                          where !installedItems.Any(i => i.Name == s.Name && i.InstalledRank == s.InstalledRank && i.UpSql == s.UpSql && i.DownSql == s.DownSql)
                          orderby s.InstalledRank
                          select s];

            if (scripts.Length == 0)
            {
                logger.WriteLine("No new migrations to apply.");
                return;
            }

            int startRank = scripts
                .Select(s => Array.FindIndex(installedItems, i => i.InstalledRank == s.InstalledRank))
                .Where(i => i != -1)
                .DefaultIfEmpty(0)
                .Min();

            foreach (var installed in installedItems.Where(i => i.InstalledRank >= startRank))
            {
                logger.WriteLine("Reverting migration with rank {0} - {1} to the previous state.", installed.InstalledRank, installed.Name);

                try
                {
                    await using var tx = await connection.BeginTransactionAsync(cancellationToken);
                    commandDef = new CommandDefinition(installed.DownSql, transaction: tx, cancellationToken: cancellationToken);
                    await connection.ExecuteAsync(commandDef);
                    await MigrationTableScript.DownAsync(connection, installed, cancellationToken);
                    await tx.CommitAsync(cancellationToken);
                }
                catch (Exception)
                {
                    await MigrationTableScript.DownFailedAsync(connection, installed, cancellationToken);
                    throw;
                }
            }

            foreach (var script in scripts)
            {
                logger.WriteLine("Applying migration with rank {0} - {1}.", script.InstalledRank, script.Name);

                var installed = Array.Find(installedItems, p => p.InstalledRank == script.InstalledRank);
                try
                {
                    await using var tx = await connection.BeginTransactionAsync(cancellationToken);
                    commandDef = new CommandDefinition(script.UpSql, transaction: tx, cancellationToken: cancellationToken);
                    await connection.ExecuteAsync(commandDef);
                    await MigrationTableScript.UpAsync(connection, script, cancellationToken);
                    await tx.CommitAsync(cancellationToken);
                }
                catch (Exception)
                {
                    await MigrationTableScript.FailedAsync(connection, script, cancellationToken);
                    throw;
                }
            }

            logger.WriteLine("Migration completed successfully.");
        }
        finally
        {
            await MigrationLockScript.LeaveAsync(connection, cancellationToken);
        }
    }
}
