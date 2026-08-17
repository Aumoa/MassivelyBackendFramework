using SQLMigration;

namespace DiscordBot.SQL.Migration;

public partial class Scripts
{
    internal class _20__AddImageGenerationWorkflows : IScript
    {
        public string Name => "AddImageGenerationWorkflows";

        public int InstalledRank => 20;

        public string UpSql => @"
CREATE TABLE `image_generation_workflows` (
    `id` TINYINT NOT NULL PRIMARY KEY,
    `workflow_json` MEDIUMTEXT NOT NULL,
    `created_at` DATETIME NOT NULL DEFAULT NOW(),
    `updated_at` DATETIME NULL
) DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
";

        public string DownSql => @"
DROP TABLE `image_generation_workflows`;
";
    }
}
