using SQLMigration;

namespace OAuth2.SQL.Migration;

public partial class Scripts
{
    internal class _4__Add_email_verify : IScript
    {
        public string Name => "Add_email_verify";

        public int InstalledRank => 4;

        public string UpSql => @"
ALTER TABLE `account`
	ADD COLUMN `name` VARCHAR(128) NOT NULL AFTER `sub`,
	ADD COLUMN `email` VARCHAR(128) NOT NULL AFTER `name`,
    ADD COLUMN `verify_code` VARCHAR(128) AFTER `email`,
    ADD UNIQUE `UNQ__account__email` (`email`);
";

        public string DownSql => @"
ALTER TABLE `account`
	DROP INDEX `UNQ__account__email`,
    DROP COLUMN `verify_code`,
    DROP COLUMN `email`,
    DROP COLUMN `name`;
";
    }
}
