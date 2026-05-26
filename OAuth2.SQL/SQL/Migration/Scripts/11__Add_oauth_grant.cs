using SQLMigration;

namespace OAuth2.SQL.Migration;

public partial class Scripts
{
    internal class _11__Add_oauth_grant : IScript
    {
        public string Name => "Add_oauth_grant";

        public int InstalledRank => 11;

        public string UpSql => @"
CREATE TABLE `oauth_grant` (
    `id` BIGINT AUTO_INCREMENT NOT NULL PRIMARY KEY,
    `account_id` VARCHAR(128) NOT NULL,
    `client_id` VARCHAR(128) NOT NULL,
    `scope` VARCHAR(64) NOT NULL,
    `created_at` DATETIME NOT NULL DEFAULT NOW(),
    `updated_at` DATETIME NOT NULL DEFAULT NOW(),
    `revoked_at` DATETIME,
    UNIQUE `UNQ__oauth_grant__account_id__client_id__scope` (`account_id`, `client_id`, `scope`),
    INDEX `IDX__oauth_grant__account_id__client_id__revoked_at` (`account_id`, `client_id`, `revoked_at`)
);
";

        public string DownSql => @"
DROP TABLE `oauth_grant`;
";
    }
}
