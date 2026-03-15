using SQLMigration;

namespace OpenAI.SQL.Migration;

public partial class Scripts
{
    internal class _1__Init : IScript
    {
        public string Name => "Init";

        public int InstalledRank => 1;

        public string UpSql => @"
CREATE TABLE `chat_session` (
    `id` VARCHAR(64) NOT NULL PRIMARY KEY,
    `user_id` VARCHAR(128) NOT NULL,
    `topic` VARCHAR(512) NOT NULL,
    `created_at` DATETIME NOT NULL DEFAULT NOW(),
    INDEX `IDX__chat_session__user_id__created_at` (`user_id`, `created_at`)
);

CREATE TABLE `chat_message` (
    `id` BIGINT AUTO_INCREMENT NOT NULL PRIMARY KEY,
    `session_id` VARCHAR(64) NOT NULL,
    `is_user` TINYINT NOT NULL,
    `content` TEXT NOT NULL,
    `created_at` DATETIME NOT NULL DEFAULT NOW(),
    INDEX `IDX__chat_message__session_id__created_at` (`session_id`, `created_at`)
);
";

        public string DownSql => @"
DROP TABLE `chat_session`;
DROP TABLE `chat_message`;
";
    }
}
