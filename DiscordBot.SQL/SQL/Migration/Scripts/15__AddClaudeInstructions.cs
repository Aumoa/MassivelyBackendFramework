using SQLMigration;

namespace DiscordBot.SQL.Migration;

public partial class Scripts
{
    internal class _15__AddClaudeInstructions : IScript
    {
        public string Name => "AddClaudeInstructions";

        public int InstalledRank => 15;

        public string UpSql => @"
ALTER TABLE `claude_settings`
    ADD COLUMN `instructions` TEXT NULL AFTER `default_max_tokens`;
";

        public string DownSql => @"
ALTER TABLE `claude_settings`
    DROP COLUMN `instructions`;
";
    }
}
