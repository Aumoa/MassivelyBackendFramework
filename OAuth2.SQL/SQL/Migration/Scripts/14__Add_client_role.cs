using SQLMigration;

namespace OAuth2.SQL.Migration;

public partial class Scripts
{
    internal class _14__Add_client_role : IScript
    {
        public string Name => "Add_client_role";

        public int InstalledRank => 14;

        public string UpSql => @"
CREATE TABLE `client_role` (
	`client_id` VARCHAR(128) NOT NULL,
    `id` VARCHAR(128) NOT NULL,
    `name` VARCHAR(128) NOT NULL,
    `created_at` DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    PRIMARY KEY (`client_id`, `id`),
    INDEX `IDX__client_role__client_name_id` (`client_id`, `name`, `id`),
    CONSTRAINT `FK__client_role__client`
        FOREIGN KEY (`client_id`) REFERENCES `client` (`id`) ON DELETE CASCADE
    );

CREATE TABLE `client_role_assignment` (
	`client_id` VARCHAR(128) NOT NULL,
    `role_id` VARCHAR(128) NOT NULL,
    `account_id` VARCHAR(128) NOT NULL,
    `created_at` DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    PRIMARY KEY (`client_id`, `role_id`, `account_id`),
    INDEX `IDX__client_role_assignment__account_client_role`
        (`account_id`, `client_id`, `role_id`),
    CONSTRAINT `FK__client_role_assignment__role`
        FOREIGN KEY (`client_id`, `role_id`) REFERENCES `client_role` (`client_id`, `id`) ON DELETE CASCADE,
    CONSTRAINT `FK__client_role_assignment__account`
        FOREIGN KEY (`account_id`) REFERENCES `account` (`id`) ON DELETE CASCADE
    );
";

        public string DownSql => @"
DROP TABLE `client_role_assignment`;
DROP TABLE `client_role`;
";
    }
}
