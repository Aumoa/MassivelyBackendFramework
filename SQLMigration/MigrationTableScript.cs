using Dapper;
using MySql.Data.MySqlClient;

namespace SQLMigration;

internal static class MigrationTableScript
{
    public record Installed
    {
        public required string Name { get; set; }

        public required int InstalledRank { get; set; }

        public required string UpSql { get; set; }

        public required string DownSql { get; set; }

        public required ExecutionStatus Status { get; set; }
    }

    public static async ValueTask<Installed[]> ReadAsync(MySqlConnection connection, CancellationToken cancellationToken = default)
    {
        const string QUERY1 = @"
CREATE TABLE IF NOT EXISTS `__MigrationHistory` (
	`Name` VARCHAR(128) NOT NULL,
    `InstalledRank` INT NOT NULL PRIMARY KEY,
    `UpSql` TEXT NOT NULL,
    `DownSql` TEXT NOT NULL,
    `InstalledAt` DATETIME NOT NULL DEFAULT NOW(),
    `Status` TINYINT NOT NULL
);
";

        var commandDef = new CommandDefinition(QUERY1, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(commandDef);

        const string QUERY2 = @"
SELECT `Name`, `InstalledRank`, `UpSql`, `DownSql`, `Status` FROM `__MigrationHistory`
    ORDER BY `InstalledRank` ASC
";

        commandDef = new CommandDefinition(QUERY2, cancellationToken: cancellationToken);
        var results = await connection.QueryAsync<Installed>(commandDef);

        return [.. results];
    }

    public static async ValueTask UpAsync(MySqlConnection connection, IScript script, CancellationToken cancellationToken = default)
    {
        const string QUERY1 = @"INSERT INTO `__MigrationHistory` (`Name`, `InstalledRank`, `UpSql`, `DownSql`, `Status`) VALUES(@Name, @InstalledRank, @UpSql, @DownSql, 0)";
        var commandDef = new CommandDefinition(QUERY1, new { script.Name, script.InstalledRank, script.UpSql, script.DownSql }, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(commandDef);
    }

    public static async ValueTask FailedAsync(MySqlConnection connection, IScript script, CancellationToken cancellationToken = default)
    {
        const string QUERY1 = @"INSERT INTO `__MigrationHistory` (`Name`, `InstalledRank`, `UpSql`, `DownSql`, `Status`) VALUES(@Name, @InstalledRank, @UpSql, @DownSql, 1)";
        var commandDef = new CommandDefinition(QUERY1, new { script.Name, script.InstalledRank, script.UpSql, script.DownSql }, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(commandDef);
    }

    public static async ValueTask DownAsync(MySqlConnection connection, Installed installed, CancellationToken cancellationToken = default)
    {
        const string QUERY1 = @"DELETE FROM `__MigrationHistory` WHERE `InstalledRank` = @InstalledRank";
        var commandDef = new CommandDefinition(QUERY1, new { installed.InstalledRank }, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(commandDef);
    }

    public static async ValueTask DownFailedAsync(MySqlConnection connection, Installed installed, CancellationToken cancellationToken = default)
    {
        const string QUERY1 = @"UPDATE `__MigrationHistory` SET `Status` = 2 WHERE `InstalledRank` = @InstalledRank";
        var commandDef = new CommandDefinition(QUERY1, new { installed
            .InstalledRank }, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(commandDef);
    }
}
