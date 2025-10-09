using SQLMigration;

namespace OAuth2.SQL.Migration;

public partial class Scripts
{
    internal class _2__Add_client : IScript
    {
        public string Name => "Add_client";

        public int InstalledRank => 2;

        public string UpSql => @"
CREATE TABLE `client` (
	`id` VARCHAR(128) NOT NULL PRIMARY KEY,
    `owner_id` VARCHAR(128) NOT NULL,
    `name` VARCHAR(512) NOT NULL,
    `created_at` DATETIME NOT NULL DEFAULT NOW(),
    `removed_at` DATETIME,
    INDEX `IDX__client__owner_id__removed_at` (`owner_id`, `removed_at`),
    UNIQUE `UNQ__client__owner_id__removed_at__name` (`owner_id`, `removed_at`, `name`)
);

CREATE TABLE `client_claim` (
	`id` BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    `client_id` VARCHAR(128) NOT NULL,
    `name` VARCHAR(128) NOT NULL,
    `value` TEXT NOT NULL,
    `created_at` DATETIME NOT NULL DEFAULT NOW(),
    `removed_at` DATETIME,
    INDEX `IDX__client_claim__client_id__removed_at__name` (`client_id`, `removed_at`, `name`)
);
";

        public string DownSql => @"
DROP TABLE `client`;
DROP TABLE `client_claim`;
";
    }
}
