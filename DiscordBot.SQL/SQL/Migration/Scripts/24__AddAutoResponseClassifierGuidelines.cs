using SQLMigration;

namespace DiscordBot.SQL.Migration;

public partial class Scripts
{
    internal class _24__AddAutoResponseClassifierGuidelines : IScript
    {
        public string Name => "AddAutoResponseClassifierGuidelines";

        public int InstalledRank => 24;

        public string UpSql => @"
ALTER TABLE `auto_response_settings`
    ADD COLUMN `classifier_guidelines` TEXT NULL AFTER `bot_name_aliases_json`;
";

        public string DownSql => @"
ALTER TABLE `auto_response_settings`
    DROP COLUMN `classifier_guidelines`;
";
    }
}
