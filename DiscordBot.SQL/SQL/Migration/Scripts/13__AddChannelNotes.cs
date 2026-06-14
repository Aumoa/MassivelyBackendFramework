using SQLMigration;

namespace DiscordBot.SQL.Migration;

public partial class Scripts
{
    internal class _13__AddChannelNotes : IScript
    {
        public string Name => "AddChannelNotes";

        public int InstalledRank => 13;

        public string UpSql => @"
CREATE TABLE `channel_note` (
    `id` BIGINT AUTO_INCREMENT NOT NULL PRIMARY KEY,
    `guild_id` VARCHAR(64) NULL,
    `channel_id` VARCHAR(64) NOT NULL,
    `created_by_user_id` VARCHAR(64) NOT NULL,
    `title` VARCHAR(256) NOT NULL,
    `content` TEXT NOT NULL,
    `tags` VARCHAR(512) NULL,
    `source_message_id` VARCHAR(64) NULL,
    `status` VARCHAR(32) NOT NULL DEFAULT 'active',
    `created_at` DATETIME NOT NULL DEFAULT NOW(),
    `updated_at` DATETIME NULL,
    INDEX `IDX__channel_note__channel_status_created` (`channel_id`, `status`, `created_at`),
    INDEX `IDX__channel_note__guild_channel_status` (`guild_id`, `channel_id`, `status`)
) DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
";

        public string DownSql => @"
DROP TABLE `channel_note`;
";
    }
}
