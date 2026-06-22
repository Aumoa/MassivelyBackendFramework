using SQLMigration;

namespace MasterServer.SQL.Migration;

public partial class Scripts
{
    internal sealed class _2__GatewayBackendRoutePolicy : IScript
    {
        public string Name => "GatewayBackendRoutePolicy";

        public int InstalledRank => 2;

        public string UpSql => @"
CREATE TABLE `gateway_backend_route_policy_entry` (
    `id` BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    `backend_kind` VARCHAR(128) NOT NULL,
    `enabled` TINYINT(1) NOT NULL DEFAULT 1,
    `created_at` DATETIME NOT NULL DEFAULT NOW(),
    `updated_at` DATETIME NOT NULL DEFAULT NOW(),
    `removed_at` DATETIME,
    `active_backend_kind` VARCHAR(128) GENERATED ALWAYS AS (IF(`removed_at` IS NULL, `backend_kind`, NULL)) STORED,
    INDEX `IDX__gateway_backend_route_policy_entry__enabled` (`enabled`, `removed_at`),
    UNIQUE `UNQ__gateway_backend_route_policy_entry__active_backend_kind` (`active_backend_kind`)
);
";

        public string DownSql => @"
DROP TABLE `gateway_backend_route_policy_entry`;
";
    }
}
