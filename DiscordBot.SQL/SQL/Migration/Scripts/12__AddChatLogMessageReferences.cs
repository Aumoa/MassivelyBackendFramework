using SQLMigration;

namespace DiscordBot.SQL.Migration;

public partial class Scripts
{
    internal class _12__AddChatLogMessageReferences : IScript
    {
        public string Name => "AddChatLogMessageReferences";

        public int InstalledRank => 12;

        public string UpSql => @"
ALTER TABLE `chat_log`
    ADD COLUMN `referenced_message_id` VARCHAR(64) NULL AFTER `message_id`,
    ADD COLUMN `referenced_channel_id` VARCHAR(64) NULL AFTER `referenced_message_id`,
    ADD COLUMN `referenced_guild_id` VARCHAR(64) NULL AFTER `referenced_channel_id`,
    ADD INDEX `IDX__chat_log__referenced_channel_message` (`referenced_channel_id`, `referenced_message_id`);
";

        public string DownSql => @"
ALTER TABLE `chat_log`
    DROP INDEX `IDX__chat_log__referenced_channel_message`,
    DROP COLUMN `referenced_guild_id`,
    DROP COLUMN `referenced_channel_id`,
    DROP COLUMN `referenced_message_id`;
";
    }
}
