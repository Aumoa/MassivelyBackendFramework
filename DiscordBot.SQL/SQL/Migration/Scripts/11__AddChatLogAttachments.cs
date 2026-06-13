using SQLMigration;

namespace DiscordBot.SQL.Migration;

public partial class Scripts
{
    internal class _11__AddChatLogAttachments : IScript
    {
        public string Name => "AddChatLogAttachments";

        public int InstalledRank => 11;

        public string UpSql => @"
CREATE TABLE `chat_log_attachment` (
    `id` BIGINT AUTO_INCREMENT NOT NULL PRIMARY KEY,
    `chat_log_id` BIGINT NOT NULL,
    `discord_attachment_id` VARCHAR(64) NULL,
    `file_name` VARCHAR(255) NULL,
    `content_type` VARCHAR(128) NOT NULL,
    `size_bytes` BIGINT NOT NULL,
    `sha256` CHAR(64) NOT NULL,
    `data` MEDIUMBLOB NOT NULL,
    `extracted_text` MEDIUMTEXT NULL,
    `extraction_status` VARCHAR(32) NOT NULL,
    `extraction_error` VARCHAR(1024) NULL,
    `created_at` DATETIME NOT NULL DEFAULT NOW(),
    INDEX `IDX__chat_log_attachment__chat_log_id` (`chat_log_id`),
    INDEX `IDX__chat_log_attachment__sha256` (`sha256`),
    FULLTEXT INDEX `FT__chat_log_attachment__extracted_text` (`extracted_text`) WITH PARSER ngram,
    CONSTRAINT `FK__chat_log_attachment__chat_log`
        FOREIGN KEY (`chat_log_id`) REFERENCES `chat_log` (`id`)
        ON DELETE CASCADE
) DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
";

        public string DownSql => @"
DROP TABLE `chat_log_attachment`;
";
    }
}
