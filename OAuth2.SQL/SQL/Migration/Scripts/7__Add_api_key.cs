using SQLMigration;

namespace OAuth2.SQL.Migration;

public partial class Scripts
{
    internal class _7__Add_api_key : IScript
    {
        public string Name => "Add_api_key";

        public int InstalledRank => 7;

        public string UpSql => @"
CREATE TABLE `client_api_key` (
    `id` BIGINT AUTO_INCREMENT NOT NULL PRIMARY KEY,
    `client_id` VARCHAR(128) NOT NULL,
    `name` VARCHAR(256) NOT NULL,
    `key_prefix` VARCHAR(8) NOT NULL,
    `key_hash` TEXT NOT NULL,
    `created_at` DATETIME NOT NULL DEFAULT NOW(),
    `removed_at` DATETIME,
    INDEX `IDX__key_prefix` (`key_prefix`, `removed_at`),
    INDEX `IDX__client_id` (`client_id`, `removed_at`)
);
";

        public string DownSql => @"
DROP TABLE `client_api_key`;
";
    }
}
