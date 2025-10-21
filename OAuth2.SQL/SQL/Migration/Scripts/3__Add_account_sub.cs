using SQLMigration;

namespace OAuth2.SQL.Migration;

public partial class Scripts
{
    internal class _3__Add_account_sub : IScript
    {
        public string Name => "Add_account_sub";

        public int InstalledRank => 3;

        public string UpSql => @"
ALTER TABLE `account`
	ADD COLUMN `sub` VARCHAR(128) NOT NULL AFTER `password`,
    ADD UNIQUE `UNQ__account__sub` (`sub`);

DROP TABLE `account_role`;
";

        public string DownSql => @"
ALTER TABLE `account`
	DROP INDEX `UNQ__account__sub`,
    DROP COLUMN `sub`;

CREATE TABLE `account_role` (
    `account_id` varchar(128) NOT NULL,
    `name` varchar(32) NOT NULL,
    `granted_at` datetime NOT NULL,
    `expired_at` datetime DEFAULT NULL,
    PRIMARY KEY (`account_id`,`name`)
);
";
    }
}
