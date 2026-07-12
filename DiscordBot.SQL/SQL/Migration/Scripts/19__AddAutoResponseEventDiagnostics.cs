using SQLMigration;

namespace DiscordBot.SQL.Migration;

public partial class Scripts
{
    internal class _19__AddAutoResponseEventDiagnostics : IScript
    {
        public string Name => "AddAutoResponseEventDiagnostics";

        public int InstalledRank => 19;

        public string UpSql => @"
ALTER TABLE `auto_response_events`
    ADD COLUMN `error_stage` VARCHAR(32) NOT NULL DEFAULT '' AFTER `focus`,
    ADD COLUMN `http_status_code` SMALLINT UNSIGNED NULL AFTER `error_stage`,
    ADD COLUMN `error_message` VARCHAR(400) NOT NULL DEFAULT '' AFTER `http_status_code`;
";

        public string DownSql => @"
ALTER TABLE `auto_response_events`
    DROP COLUMN `error_message`,
    DROP COLUMN `http_status_code`,
    DROP COLUMN `error_stage`;
";
    }
}
