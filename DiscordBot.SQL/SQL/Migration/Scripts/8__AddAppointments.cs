using SQLMigration;

namespace DiscordBot.SQL.Migration;

public partial class Scripts
{
    internal class _8__AddAppointments : IScript
    {
        public string Name => "AddAppointments";

        public int InstalledRank => 8;

        public string UpSql => @"
CREATE TABLE `appointment` (
    `id` BIGINT AUTO_INCREMENT NOT NULL PRIMARY KEY,
    `guild_id` VARCHAR(64) NULL,
    `channel_id` VARCHAR(64) NOT NULL,
    `user_id` VARCHAR(64) NOT NULL,
    `source_message_id` VARCHAR(64) NULL,
    `title` VARCHAR(256) NOT NULL,
    `description` VARCHAR(2048) NULL,
    `starts_at_utc` DATETIME NOT NULL,
    `timezone` VARCHAR(64) NOT NULL,
    `status` VARCHAR(32) NOT NULL DEFAULT 'active',
    `created_at` DATETIME NOT NULL DEFAULT NOW(),
    `updated_at` DATETIME NULL,
    `expires_at_utc` DATETIME NOT NULL,
    INDEX `IDX__appointment__user_guild_status_start` (`user_id`, `guild_id`, `status`, `starts_at_utc`),
    INDEX `IDX__appointment__expires_at_utc` (`expires_at_utc`)
) DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
";

        public string DownSql => @"
DROP TABLE `appointment`;
";
    }
}
