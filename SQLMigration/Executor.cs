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

            scripts = [.. scripts.OrderBy(s => s.InstalledRank)];
            if (scripts.Length == 0)
            {
                logger.WriteLine("No migrations to apply.");
                return;
            }

            int startIndex = scripts.Length;
            for (int i = 0; i < scripts.Length; ++i)
            {
                var s = scripts[i];
                var f = Array.FindIndex(installedItems, i => i.InstalledRank == s.InstalledRank);
                if (f == -1)
                {
                    startIndex = i;
                    break;
                }

                var installed = installedItems[f];
                if (installed.UpSql != s.UpSql || installed.DownSql != s.DownSql)
                {
                    startIndex = i;
                    break;
                }
            }

            if (startIndex >= scripts.Length)
            {
                logger.WriteLine("No new migrations to apply.");
                return;
            }

            foreach (var installed in installedItems.Where(i => i.InstalledRank >= scripts[startIndex].InstalledRank))
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

            foreach (var script in scripts.Skip(startIndex))
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
