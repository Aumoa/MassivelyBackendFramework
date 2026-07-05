using SQLMigration;

namespace DiscordBot.SQL.Migration;

public partial class Scripts
{
    internal class _17__AddAutoResponseSettings : IScript
    {
        public string Name => "AddAutoResponseSettings";

        public int InstalledRank => 17;

        public string UpSql => @"
CREATE TABLE `auto_response_settings` (
    `id` TINYINT NOT NULL PRIMARY KEY,
    `enabled` BOOLEAN NOT NULL DEFAULT FALSE,
    `interval_seconds` INT NOT NULL,
    `cooldown_seconds` INT NOT NULL,
    `max_buffered_messages` INT NOT NULL,
    `classifier_max_tokens` INT NOT NULL,
    `classifier_model` VARCHAR(128) NULL,
    `bot_name_aliases_json` TEXT NOT NULL,
    `created_at` DATETIME NOT NULL DEFAULT NOW(),
    `updated_at` DATETIME NULL
) DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE `auto_response_events` (
    `id` BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    `guild_id` VARCHAR(32) NULL,
    `channel_id` VARCHAR(32) NOT NULL,
    `trigger_message_id` VARCHAR(32) NULL,
    `message_ids_json` TEXT NOT NULL,
    `decision` VARCHAR(32) NOT NULL,
    `reason` VARCHAR(400) NOT NULL,
    `focus` VARCHAR(400) NOT NULL,
    `created_at` DATETIME NOT NULL DEFAULT NOW(),
    INDEX `idx_auto_response_events_channel_created` (`channel_id`, `created_at`),
    INDEX `idx_auto_response_events_created` (`created_at`)
) DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
";

        public string DownSql => @"
DROP TABLE `auto_response_events`;
DROP TABLE `auto_response_settings`;
";
    }
}
