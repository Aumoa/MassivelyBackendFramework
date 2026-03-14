using SQLMigration;

namespace OpenAI.SQL.Migration;

public partial class Scripts
{
    internal class _2__Add_message_role : IScript
    {
        public string Name => "Add_message_role";

        public int InstalledRank => 2;

        public string UpSql => @"
ALTER TABLE `chat_message`
    ADD COLUMN `role` TINYINT NOT NULL DEFAULT 0 AFTER `session_id`;

UPDATE `chat_message` SET `role` = CASE WHEN `is_user` = 1 THEN 0 ELSE 1 END;

ALTER TABLE `chat_message` DROP COLUMN `is_user`;
";

        public string DownSql => @"
ALTER TABLE `chat_message`
    ADD COLUMN `is_user` TINYINT NOT NULL DEFAULT 0 AFTER `session_id`;

UPDATE `chat_message` SET `is_user` = CASE WHEN `role` = 0 THEN 1 ELSE 0 END;

ALTER TABLE `chat_message` DROP COLUMN `role`;
";
    }
}
