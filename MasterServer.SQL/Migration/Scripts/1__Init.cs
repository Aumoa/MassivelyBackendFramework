using SQLMigration;

namespace MasterServer.SQL.Migration;

public partial class Scripts
{
    internal sealed class _1__Init : IScript
    {
        public string Name => "Init";

        public int InstalledRank => 1;

        public string UpSql => @"
CREATE TABLE `service_connection_credential` (
    `id` BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    `node_kind` TINYINT UNSIGNED NOT NULL,
    `node_id` VARCHAR(128) NOT NULL,
    `display_name` VARCHAR(256) NOT NULL,
    `protected_secret` TEXT NOT NULL,
    `enabled` TINYINT(1) NOT NULL DEFAULT 1,
    `created_at` DATETIME NOT NULL DEFAULT NOW(),
    `updated_at` DATETIME NOT NULL DEFAULT NOW(),
    `removed_at` DATETIME,
    `active_node_id` VARCHAR(128) GENERATED ALWAYS AS (IF(`removed_at` IS NULL, `node_id`, NULL)) STORED,
    INDEX `IDX__service_connection_credential__node_kind__enabled` (`node_kind`, `enabled`, `removed_at`),
    UNIQUE `UNQ__service_connection_credential__node_kind__active_node_id` (`node_kind`, `active_node_id`)
);
";

        public string DownSql => @"
DROP TABLE `service_connection_credential`;
";
    }
}
