using SQLMigration;

namespace MasterServer.SQL.Migration;

public partial class Scripts
{
    internal sealed class _4__BackendPacketManifest : IScript
    {
        public string Name => "BackendPacketManifest";

        public int InstalledRank => 4;

        public string UpSql => @"
CREATE TABLE `backend_packet_manifest` (
    `id` BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    `backend_kind` VARCHAR(128) NOT NULL,
    `manifest_id` VARCHAR(128) NOT NULL,
    `manifest_hash` CHAR(64) NOT NULL,
    `lifecycle` TINYINT UNSIGNED NOT NULL DEFAULT 1,
    `audit_note` VARCHAR(512) NOT NULL DEFAULT '',
    `created_at` DATETIME NOT NULL DEFAULT NOW(),
    `updated_at` DATETIME NOT NULL DEFAULT NOW(),
    `deprecated_at` DATETIME,
    `removed_at` DATETIME,
    `active_manifest_key` VARCHAR(384) GENERATED ALWAYS AS (IF(`removed_at` IS NULL, CONCAT(`backend_kind`, ':', `manifest_id`, ':', `manifest_hash`), NULL)) STORED,
    INDEX `IDX__backend_packet_manifest__gateway_snapshot` (`removed_at`, `lifecycle`, `backend_kind`),
    INDEX `IDX__backend_packet_manifest__lookup` (`backend_kind`, `manifest_id`, `manifest_hash`, `removed_at`, `lifecycle`),
    UNIQUE `UNQ__backend_packet_manifest__active_manifest_key` (`active_manifest_key`)
);

CREATE TABLE `backend_packet_manifest_entry` (
    `id` BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    `manifest_record_id` BIGINT NOT NULL,
    `direction` TINYINT UNSIGNED NOT NULL,
    `packet_kind` TINYINT UNSIGNED NOT NULL,
    `packet_id` SMALLINT UNSIGNED NOT NULL,
    `routed_version` SMALLINT UNSIGNED NOT NULL,
    `minimum_payload_length` INT NOT NULL,
    `maximum_payload_length` INT NOT NULL,
    `fixed_payload_length` INT,
    `schema_id` VARCHAR(128),
    `schema_hash` CHAR(64),
    `verifier_program` MEDIUMTEXT,
    `entry_status` TINYINT UNSIGNED NOT NULL DEFAULT 1,
    `created_at` DATETIME NOT NULL DEFAULT NOW(),
    INDEX `IDX__backend_packet_manifest_entry__manifest_record_id` (`manifest_record_id`),
    UNIQUE `UNQ__backend_packet_manifest_entry__packet_key` (`manifest_record_id`, `direction`, `packet_kind`, `packet_id`, `routed_version`),
    CONSTRAINT `FK__backend_packet_manifest_entry__manifest_record_id`
        FOREIGN KEY (`manifest_record_id`)
        REFERENCES `backend_packet_manifest` (`id`)
        ON DELETE CASCADE
);
";

        public string DownSql => @"
DROP TABLE `backend_packet_manifest_entry`;
DROP TABLE `backend_packet_manifest`;
";
    }
}
