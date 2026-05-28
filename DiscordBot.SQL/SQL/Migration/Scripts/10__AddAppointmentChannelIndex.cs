using SQLMigration;

namespace DiscordBot.SQL.Migration;

public partial class Scripts
{
    internal class _10__AddAppointmentChannelIndex : IScript
    {
        public string Name => "AddAppointmentChannelIndex";

        public int InstalledRank => 10;

        public string UpSql => @"
ALTER TABLE `appointment`
    ADD INDEX `IDX__appointment__channel_guild_status_start` (`channel_id`, `guild_id`, `status`, `starts_at_utc`);
";

        public string DownSql => @"
ALTER TABLE `appointment`
    DROP INDEX `IDX__appointment__channel_guild_status_start`;
";
    }
}
