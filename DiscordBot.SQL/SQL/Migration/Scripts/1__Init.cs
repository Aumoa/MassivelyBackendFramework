using SQLMigration;

namespace DiscordBot.SQL.Migration;

public partial class Scripts
{
    internal class _1__Init : IScript
    {
        public string Name => "Init";

        public int InstalledRank => 1;

        public string UpSql => @"
CREATE TABLE `chat_log` (
    `id` BIGINT AUTO_INCREMENT NOT NULL PRIMARY KEY,
    `channel_id` VARCHAR(64) NOT NULL,
    `user_id` VARCHAR(64) NOT NULL,
    `content` VARCHAR(4096) NOT NULL,
    `created_at` DATETIME NOT NULL DEFAULT NOW(),
    INDEX `IDX__chat_log__channel_id__created_at` (`channel_id`, `created_at`),
    INDEX `IDX__chat_log__user_id__created_at` (`user_id`, `created_at`)
) DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
";

        public string DownSql => @"
DROP TABLE `chat_log`;
";
    }
}
