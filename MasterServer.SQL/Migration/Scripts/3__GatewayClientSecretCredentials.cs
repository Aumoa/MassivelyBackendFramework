using SQLMigration;

namespace MasterServer.SQL.Migration;

public partial class Scripts
{
    internal sealed class _3__GatewayClientSecretCredentials : IScript
    {
        public string Name => "GatewayClientSecretCredentials";

        public int InstalledRank => 3;

        public string UpSql => @"
CREATE TABLE `gateway_client_secret_credential` (
    `id` BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    `token_id` VARCHAR(64) NOT NULL,
    `subject_id` VARCHAR(128) NOT NULL,
    `display_name` VARCHAR(256) NOT NULL,
    `secret_hash` VARCHAR(128) NOT NULL,
    `enabled` TINYINT(1) NOT NULL DEFAULT 1,
    `created_at` DATETIME NOT NULL DEFAULT NOW(),
    `updated_at` DATETIME NOT NULL DEFAULT NOW(),
    `removed_at` DATETIME,
    `active_token_id` VARCHAR(64) GENERATED ALWAYS AS (IF(`removed_at` IS NULL, `token_id`, NULL)) STORED,
    INDEX `IDX__gateway_client_secret_credential__enabled` (`enabled`, `removed_at`),
    UNIQUE `UNQ__gateway_client_secret_credential__active_token_id` (`active_token_id`)
);
";

        public string DownSql => @"
DROP TABLE `gateway_client_secret_credential`;
";
    }
}
