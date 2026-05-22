using SQLMigration;

namespace DiscordBot.SQL.Migration;

public partial class Scripts
{
    internal class _4__AddClaudeSettings : IScript
    {
        public string Name => "AddClaudeSettings";

        public int InstalledRank => 4;

        public string UpSql => @"
CREATE TABLE `claude_settings` (
    `id` TINYINT NOT NULL PRIMARY KEY,
    `model` VARCHAR(128) NOT NULL,
    `summary_model` VARCHAR(128) NOT NULL,
    `default_max_tokens` INT NOT NULL,
    `created_at` DATETIME NOT NULL DEFAULT NOW(),
    `updated_at` DATETIME NULL
) DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
";

        public string DownSql => @"
DROP TABLE `claude_settings`;
";
    }
}
