using SQLMigration;

namespace DiscordBot.SQL.Migration;

public partial class Scripts
{
    internal class _9__AddAppointmentHasTime : IScript
    {
        public string Name => "AddAppointmentHasTime";

        public int InstalledRank => 9;

        public string UpSql => @"
ALTER TABLE `appointment`
    ADD COLUMN `has_time` BOOLEAN NOT NULL DEFAULT TRUE AFTER `starts_at_utc`;
";

        public string DownSql => @"
ALTER TABLE `appointment`
    DROP COLUMN `has_time`;
";
    }
}
