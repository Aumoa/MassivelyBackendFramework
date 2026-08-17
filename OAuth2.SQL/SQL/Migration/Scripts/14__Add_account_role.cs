using SQLMigration;

namespace OAuth2.SQL.Migration;

public partial class Scripts
{
    internal class _14__Add_account_role : IScript
    {
        public string Name => "Add_account_role";

        public int InstalledRank => 14;

        public string UpSql => @"
CREATE TABLE `account_role` (
	`id` BIGINT AUTO_INCREMENT NOT NULL PRIMARY KEY,
    `account_id` VARCHAR(128) NOT NULL,
    `name` VARCHAR(128) NOT NULL,
    `created_at` DATETIME NOT NULL DEFAULT NOW(),
    `removed_at` DATETIME,
    INDEX `IDX__account_id` (`account_id`),
    UNIQUE `UNQ__account_id__name__removed_at` (`account_id`, `name`, `removed_at`)
    );
";

        public string DownSql => @"
DROP TABLE `account_role`;
";
    }
}
