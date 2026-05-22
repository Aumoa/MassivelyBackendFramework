using SQLMigration;

namespace DiscordBot.SQL.Migration;

public partial class Scripts
{
    internal class _5__AddToolSettings : IScript
    {
        public string Name => "AddToolSettings";

        public int InstalledRank => 5;

        public string UpSql => @"
CREATE TABLE `tool_settings` (
    `tool_name` VARCHAR(128) NOT NULL PRIMARY KEY,
    `enabled` BOOLEAN NOT NULL DEFAULT TRUE,
    `created_at` DATETIME NOT NULL DEFAULT NOW(),
    `updated_at` DATETIME NULL
) DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
";

        public string DownSql => @"
DROP TABLE `tool_settings`;
";
    }
}
