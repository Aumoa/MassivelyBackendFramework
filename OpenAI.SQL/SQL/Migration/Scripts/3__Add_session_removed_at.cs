using SQLMigration;

namespace OpenAI.SQL.Migration;

public partial class Scripts
{
    internal class _3__Add_session_removed_at : IScript
    {
        public string Name => "Add_session_removed_at";

        public int InstalledRank => 3;

        public string UpSql => @"
ALTER TABLE `chat_session`
    ADD COLUMN `removed_at` DATETIME NULL DEFAULT NULL;
";

        public string DownSql => @"
ALTER TABLE `chat_session` DROP COLUMN `removed_at`;
";
    }
}
