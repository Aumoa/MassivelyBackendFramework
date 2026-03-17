using SQLMigration;

namespace OAuth2.SQL.Migration;

public partial class Scripts
{
    internal class _9__Add_api_key_scope_and_client_restriction : IScript
    {
        public string Name => "Add_api_key_scope_and_client_restriction";

        public int InstalledRank => 9;

        public string UpSql => @"
ALTER TABLE `client_api_key`
    MODIFY COLUMN `client_id` VARCHAR(128) NULL DEFAULT NULL,
    ADD COLUMN `allowed_client_id` VARCHAR(128) NULL AFTER `client_id`,
    ADD COLUMN `allowed_scope` VARCHAR(512) NULL AFTER `allowed_client_id`;
";

        public string DownSql => @"
ALTER TABLE `client_api_key`
    DROP COLUMN `allowed_client_id`,
    DROP COLUMN `allowed_scope`,
    MODIFY COLUMN `client_id` VARCHAR(128) NOT NULL;
";
    }
}
