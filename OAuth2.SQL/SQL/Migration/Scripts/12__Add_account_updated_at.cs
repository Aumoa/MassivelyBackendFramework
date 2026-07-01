using SQLMigration;

namespace OAuth2.SQL.Migration;

public partial class Scripts
{
    internal class _12__Add_account_updated_at : IScript
    {
        public string Name => "Add_account_updated_at";

        public int InstalledRank => 12;

        public string UpSql => @"
ALTER TABLE `account`
    ADD COLUMN `updated_at` DATETIME NULL AFTER `created_at`;

UPDATE `account`
SET `updated_at` = `created_at`
WHERE `updated_at` IS NULL;

ALTER TABLE `account`
    MODIFY COLUMN `updated_at` DATETIME NOT NULL DEFAULT NOW(),
    ADD INDEX `IDX__account__updated_at` (`updated_at`);
";

        public string DownSql => @"
ALTER TABLE `account`
    DROP INDEX `IDX__account__updated_at`,
    DROP COLUMN `updated_at`;
";
    }
}
