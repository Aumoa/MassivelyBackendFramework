using SQLMigration;

namespace DiscordBot.SQL.Migration;

public partial class Scripts
{
    internal class _21__AddImageGenerationWorkflowNames : IScript
    {
        public string Name => "AddImageGenerationWorkflowNames";

        public int InstalledRank => 21;

        public string UpSql => @"
CREATE TABLE `image_generation_workflows_new` (
    `name` VARCHAR(64) NOT NULL PRIMARY KEY,
    `description` VARCHAR(1024) NULL,
    `workflow_json` MEDIUMTEXT NOT NULL,
    `sort_order` INT NOT NULL DEFAULT 0,
    `created_at` DATETIME NOT NULL DEFAULT NOW(),
    `updated_at` DATETIME NULL
) DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO `image_generation_workflows_new` (`name`, `workflow_json`, `sort_order`, `created_at`, `updated_at`)
SELECT 'default', `workflow_json`, 0, `created_at`, `updated_at`
FROM `image_generation_workflows`;

DROP TABLE `image_generation_workflows`;

RENAME TABLE `image_generation_workflows_new` TO `image_generation_workflows`;
";

        public string DownSql => @"
CREATE TABLE `image_generation_workflows_old` (
    `id` TINYINT NOT NULL PRIMARY KEY,
    `workflow_json` MEDIUMTEXT NOT NULL,
    `created_at` DATETIME NOT NULL DEFAULT NOW(),
    `updated_at` DATETIME NULL
) DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO `image_generation_workflows_old` (`id`, `workflow_json`, `created_at`, `updated_at`)
SELECT 1, `workflow_json`, `created_at`, `updated_at`
FROM `image_generation_workflows`
ORDER BY `sort_order` ASC, `name` ASC
LIMIT 1;

DROP TABLE `image_generation_workflows`;

RENAME TABLE `image_generation_workflows_old` TO `image_generation_workflows`;
";
    }
}
