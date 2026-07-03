using SQLMigration;

namespace DiscordBot.SQL.Migration;

public partial class Scripts
{
    internal class _16__AddAiSkills : IScript
    {
        public string Name => "AddAiSkills";

        public int InstalledRank => 16;

        public string UpSql => @"
CREATE TABLE `ai_skill` (
    `name` VARCHAR(64) NOT NULL PRIMARY KEY,
    `description` VARCHAR(1024) NOT NULL,
    `priority` INT NOT NULL DEFAULT 0,
    `trigger_phrases_json` TEXT NOT NULL,
    `instructions` MEDIUMTEXT NOT NULL,
    `enabled` TINYINT(1) NOT NULL DEFAULT 1,
    `created_at` DATETIME NOT NULL DEFAULT NOW(),
    `updated_at` DATETIME NULL,
    INDEX `IDX__ai_skill__enabled_priority_name` (`enabled`, `priority`, `name`)
) DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
";

        public string DownSql => @"
DROP TABLE `ai_skill`;
";
    }
}
