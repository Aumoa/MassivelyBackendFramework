using SQLMigration;

namespace DiscordBot.SQL.Migration;

public partial class Scripts
{
    internal class _6__AddChatLogImages : IScript
    {
        public string Name => "AddChatLogImages";

        public int InstalledRank => 6;

        public string UpSql => @"
CREATE TABLE `chat_log_image` (
    `id` BIGINT AUTO_INCREMENT NOT NULL PRIMARY KEY,
    `chat_log_id` BIGINT NOT NULL,
    `file_name` VARCHAR(255) NULL,
    `content_type` VARCHAR(64) NOT NULL,
    `width` INT NOT NULL,
    `height` INT NOT NULL,
    `data` MEDIUMBLOB NOT NULL,
    `created_at` DATETIME NOT NULL DEFAULT NOW(),
    INDEX `IDX__chat_log_image__chat_log_id` (`chat_log_id`),
    CONSTRAINT `FK__chat_log_image__chat_log`
        FOREIGN KEY (`chat_log_id`) REFERENCES `chat_log` (`id`)
        ON DELETE CASCADE
) DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
";

        public string DownSql => @"
DROP TABLE `chat_log_image`;
";
    }
}
