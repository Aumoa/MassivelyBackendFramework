using SQLMigration;

namespace SecretGate.SQL.Migration;

public partial class Scripts
{
    internal sealed class _3__AddShareAttemptTracking : IScript
    {
        public string Name => "AddShareAttemptTracking";

        public int InstalledRank => 3;

        public string UpSql => @"
ALTER TABLE `secretgate_share_secret`
    ADD COLUMN `failed_access_attempts` INT NOT NULL DEFAULT 0 AFTER `access_key_hash`,
    ADD COLUMN `last_failed_at` DATETIME(6) NULL AFTER `failed_access_attempts`;
";

        public string DownSql => @"
ALTER TABLE `secretgate_share_secret`
    DROP COLUMN `last_failed_at`,
    DROP COLUMN `failed_access_attempts`;
";
    }
}
