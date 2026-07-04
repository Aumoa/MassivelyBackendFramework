using SQLMigration;

namespace DiscordBot.SQL.Migration;

public partial class Scripts
{
    internal class _17__AddDiscordUserPermissions : IScript
    {
        public string Name => "AddDiscordUserPermissions";

        public int InstalledRank => 17;

        public string UpSql => @"
CREATE TABLE `discord_user_permissions` (
    `id` BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    `user_id` VARCHAR(64) NOT NULL,
    `display_name` VARCHAR(128) NULL,
    `permission` VARCHAR(64) NOT NULL DEFAULT 'user',
    `enabled` BOOLEAN NOT NULL DEFAULT TRUE,
    `memo` VARCHAR(512) NULL,
    `created_by` VARCHAR(128) NULL,
    `created_at` DATETIME NOT NULL DEFAULT NOW(),
    `updated_at` DATETIME NULL,
    UNIQUE KEY `ux_discord_user_permissions_user_id` (`user_id`),
    KEY `ix_discord_user_permissions_permission` (`permission`)
) DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
";

        public string DownSql => @"
DROP TABLE `discord_user_permissions`;
";
    }
}
