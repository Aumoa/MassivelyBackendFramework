using SQLMigration;

namespace OAuth2.SQL.Migration;

public partial class Scripts
{
    internal class _5__Add_client_user_group : IScript
    {
        public string Name => "Add_client_user_group";

        public int InstalledRank => 5;

        public string UpSql => @"
CREATE TABLE `client_user_group` (
	`id` BIGINT AUTO_INCREMENT NOT NULL PRIMARY KEY,
    `client_id` VARCHAR(128) NOT NULL,
    `account_id` VARCHAR(128) NOT NULL,
    `group` VARCHAR(128) NOT NULL,
    `created_at` DATETIME NOT NULL DEFAULT NOW(),
    `removed_at` DATETIME,
    INDEX `IDX__client_id__account_id` (`client_id`, `account_id`),
    UNIQUE `UNQ__client_id__account_id__group` (`client_id`, `account_id`, `group`)
    );
";

        public string DownSql => @"
DROP TABLE `client_user_group`;
";
    }
}