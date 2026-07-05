using SQLMigration;

namespace DiscordBot.SQL.Migration;

public partial class Scripts
{
    internal class _7__AddAllowedChannelRequests : IScript
    {
        public string Name => "AddAllowedChannelRequests";

        public int InstalledRank => 7;

        public string UpSql => @"
CREATE TABLE `allowed_channel_request` (
    `id` BIGINT AUTO_INCREMENT NOT NULL PRIMARY KEY,
    `token` CHAR(32) NOT NULL,
    `guild_id` VARCHAR(64) NULL,
    `channel_id` VARCHAR(64) NOT NULL,
    `guild_name` VARCHAR(128) NULL,
    `channel_name` VARCHAR(128) NULL,
    `requester_id` VARCHAR(64) NOT NULL,
    `requester_name` VARCHAR(128) NULL,
    `message_id` VARCHAR(64) NULL,
    `status` VARCHAR(32) NOT NULL DEFAULT 'pending',
    `approved_by` VARCHAR(128) NULL,
    `approved_at` DATETIME NULL,
    `created_at` DATETIME NOT NULL DEFAULT NOW(),
    `expires_at` DATETIME NOT NULL,
    UNIQUE KEY `UK__allowed_channel_request__token` (`token`),
    INDEX `IDX__allowed_channel_request__channel_id__status` (`channel_id`, `status`),
    INDEX `IDX__allowed_channel_request__expires_at` (`expires_at`)
) DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
";

        public string DownSql => @"
DROP TABLE `allowed_channel_request`;
";
    }
}
