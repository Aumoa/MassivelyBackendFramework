using SQLMigration;

namespace DiscordBot.SQL.Migration;

public partial class Scripts
{
    internal class _2__AddChatLogSearch : IScript
    {
        public string Name => "AddChatLogSearch";

        public int InstalledRank => 2;

        public string UpSql => @"
ALTER TABLE `chat_log`
    ADD COLUMN `message_id` VARCHAR(64) NULL AFTER `id`,
    ADD COLUMN `guild_id` VARCHAR(64) NULL AFTER `channel_id`,
    ADD FULLTEXT INDEX `FT__chat_log__content` (`content`) WITH PARSER ngram;
";

        public string DownSql => @"
ALTER TABLE `chat_log`
    DROP INDEX `FT__chat_log__content`,
    DROP COLUMN `guild_id`,
    DROP COLUMN `message_id`;
";
    }
}
