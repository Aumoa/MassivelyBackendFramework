using SQLMigration;

namespace OAuth2.SQL.Migration;

public partial class Scripts
{
    internal class _1__Init : IScript
    {
        public string Name => "Init";

        public int InstalledRank => 1;

        public string UpSql => @"
CREATE TABLE `account` (
    `id` VARCHAR(128) NOT NULL PRIMARY KEY,
    `password` VARCHAR(128) NOT NULL,
    `created_at` DATETIME NOT NULL DEFAULT NOW(),
    INDEX `IDX__account__created_at` (`created_at`)
);

CREATE TABLE `account_claim` (
	`id` BIGINT AUTO_INCREMENT NOT NULL PRIMARY KEY,
    `account_id` VARCHAR(128) NOT NULL,
    `name` VARCHAR(128) NOT NULL,
    `value` TEXT NOT NULL,
    `created_at` DATETIME NOT NULL DEFAULT NOW(),
    `removed_at` DATETIME,
    INDEX `IDX__account_claim__account_id__name__removed_at__created_at` (`account_id`, `name`, `removed_at`, `created_at`)
);

CREATE TABLE `account_role` (
    `account_id` VARCHAR(128) NOT NULL,
    `name` VARCHAR(32) NOT NULL,
    `granted_at` DATETIME NOT NULL,
    `expired_at` DATETIME,
    PRIMARY KEY (`account_id`, `name`)
);
";

        public string DownSql => @"
DROP TABLE `account`;
DROP TABLE `account_claim`;
DROP TABLE `account_role`;
";
    }
}
