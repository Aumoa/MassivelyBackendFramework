using SQLMigration;

namespace DiscordBot.SQL.Migration;

public partial class Scripts
{
    internal class _3__AddAllowedChannels : IScript
    {
        public string Name => "AddAllowedChannels";

        public int InstalledRank => 3;

        public string UpSql => @"
CREATE TABLE `allowed_channel` (
    `id` BIGINT AUTO_INCREMENT NOT NULL PRIMARY KEY,
    `channel_id` VARCHAR(64) NOT NULL,
    `guild_id` VARCHAR(64) NULL,
    `channel_name` VARCHAR(128) NULL,
    `guild_name` VARCHAR(128) NULL,
    `memo` VARCHAR(512) NULL,
    `enabled` BOOLEAN NOT NULL DEFAULT TRUE,
    `created_by` VARCHAR(128) NULL,
    `created_at` DATETIME NOT NULL DEFAULT NOW(),
    `updated_at` DATETIME NULL,
    UNIQUE KEY `UK__allowed_channel__channel_id` (`channel_id`),
    INDEX `IDX__allowed_channel__enabled` (`enabled`)
) DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
";

        public string DownSql => @"
DROP TABLE `allowed_channel`;
";
    }
}
